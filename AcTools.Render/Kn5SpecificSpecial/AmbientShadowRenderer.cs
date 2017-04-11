﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AcTools.DataFile;
using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Materials;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Base.Utils;
using AcTools.Render.Data;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Shaders;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using JetBrains.Annotations;
using SlimDX;
using SlimDX.Direct3D11;
using SlimDX.DXGI;

namespace AcTools.Render.Kn5SpecificSpecial {
    public class AmbientShadowRenderer : BaseRenderer {
        private readonly Kn5 _kn5;
        private readonly RenderableList _scene;
        private readonly CarData _carData;
        private RenderableList _carNode;

        protected override FeatureLevel FeatureLevel => FeatureLevel.Level_10_0;

        public AmbientShadowRenderer(string mainKn5Filename, string carLocation = null) : this(Kn5.FromFile(mainKn5Filename), carLocation) { }

        public AmbientShadowRenderer(Kn5 kn5, string carLocation = null) {
            _kn5 = kn5;
            _carData = new CarData(DataWrapper.FromDirectory(carLocation ?? Path.GetDirectoryName(kn5.OriginalFilename) ?? ""));
            _scene = new RenderableList();
        }

        public float DiffusionLevel = 0.35f;
        public float SkyBrightnessLevel = 4.0f;
        public float BodyMultipler = 0.8f;
        public float WheelMultipler = 0.69f;
        public float UpDelta = 0.1f;
        public int Iterations = 2000;
        public bool HideWheels = true;
        public bool Fade = true;
        public bool BlurResult = false;
        public bool DebugMode = false;

        public const int BodySize = 512;
        public const int BodyPadding = 64;
        public const int WheelSize = 64;
        public const int WheelPadding = 32;

        protected override void ResizeInner() { }

        private void LoadAndAdjustKn5() {
            DeviceContextHolder.Set<IMaterialsFactory>(new DepthMaterialsFactory());

            _carNode = (RenderableList)Kn5RenderableDepthOnlyObject.Convert(_kn5.RootNode);
            _scene.Add(_carNode);

            _carNode.UpdateBoundingBox();
            _carNode.LocalMatrix = Matrix.Translation(0, UpDelta - (_carNode.BoundingBox?.Minimum.Y ?? 0f), 0) * _carNode.LocalMatrix;
            _scene.UpdateBoundingBox();
        }

        private void LoadAmbientShadowSize() {
            _ambientBodyShadowSize = _carData.GetBodyShadowSize();
        }

        private void InitializeBuffers() {
            _shadowBuffer = TargetResourceDepthTexture.Create();
            _summBuffer = TargetResourceTexture.Create(Format.R32_Float);
            _tempBuffer = TargetResourceTexture.Create(Format.R32_Float);

            _blendState = Device.CreateBlendState(new RenderTargetBlendDescription {
                BlendEnable = true,
                SourceBlend = BlendOption.One,
                DestinationBlend = BlendOption.One,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = BlendOption.SourceAlpha,
                DestinationBlendAlpha = BlendOption.InverseSourceAlpha,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteMaskFlags.All,
            });

            _effect = DeviceContextHolder.GetEffect<EffectSpecialShadow>();
        }

        protected override void InitializeInner() {
            LoadAndAdjustKn5();
            LoadAmbientShadowSize();
            InitializeBuffers();
        }

        private void PrepareBuffers(int size, int shadowResolution) {
            Width = size;
            Height = size;
            Resize();

            _shadowBuffer.Resize(DeviceContextHolder, shadowResolution, shadowResolution, null);
            _shadowViewport = new Viewport(0, 0, _shadowBuffer.Width, _shadowBuffer.Height, 0, 1.0f);

            _summBuffer.Resize(DeviceContextHolder, size, size, null);
            _tempBuffer.Resize(DeviceContextHolder, size, size, null);
            DeviceContext.ClearRenderTargetView(_summBuffer.TargetView, new Color4(0f, 0f, 0f, 0f));
        }

        private Vector3 _ambientBodyShadowSize, _shadowSize;
        private Viewport _shadowViewport;
        private TargetResourceDepthTexture _shadowBuffer;
        private CameraOrtho _shadowCamera;
        private Matrix _shadowDestinationTransform;
        private TargetResourceTexture _summBuffer, _tempBuffer;
        private BlendState _blendState;
        private EffectSpecialShadow _effect;
        private bool _wheelMode;

        private Kn5RenderableDepthOnlyObject[] _flattenNodes;

        [NotNull]
        private static IEnumerable<Kn5RenderableDepthOnlyObject> Flatten(RenderableList root, Func<IRenderableObject, bool> filter = null) {
            return root
                    .SelectManyRecursive(x => {
                        var list = x as Kn5RenderableList;
                        if (list == null || !list.IsEnabled) return null;
                        return filter?.Invoke(list) == false ? null : list;
                    })
                    .OfType<Kn5RenderableDepthOnlyObject>()
                    .Where(x => x.IsEnabled && filter?.Invoke(x) != false);
        }

        private void DrawShadow(Vector3 from, Vector3? up = null) {
            DeviceContext.OutputMerger.DepthStencilState = null;
            DeviceContext.OutputMerger.BlendState = null;
            DeviceContext.Rasterizer.State = DeviceContextHolder.States.DoubleSidedState;
            DeviceContext.Rasterizer.SetViewports(_shadowViewport);

            DeviceContext.ClearDepthStencilView(_shadowBuffer.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);
            DeviceContext.OutputMerger.SetTargets(_shadowBuffer.DepthView);

            _shadowCamera.LookAt(Vector3.Normalize(from) * _shadowCamera.FarZValue * 0.8f, Vector3.Zero, up ?? Vector3.UnitY);
            _shadowCamera.UpdateViewMatrix();

            if (_flattenNodes == null) {
                string[] ignored;

                if (HideWheels && !_wheelMode) {
                    ignored = new[] {
                        "WHEEL_LF", "WHEEL_LR", "WHEEL_RF", "WHEEL_RR",
                        "HUB_LF", "HUB_LR", "HUB_RF", "HUB_RR",
                        "SUSP_LF", "SUSP_LR", "SUSP_RF", "SUSP_RR",
                        "COCKPIT_HR", "STEER_HR",
                    };
                } else {
                    ignored = new[] {
                        "COCKPIT_HR", "STEER_HR",
                    };
                }

                _flattenNodes = Flatten(_scene, x => !ignored.Contains((x as Kn5RenderableList)?.Name)).ToArray();
            }

            for (var i = 0; i < _flattenNodes.Length; i++) {
                _flattenNodes[i].Draw(DeviceContextHolder, _shadowCamera, SpecialRenderMode.Simple);
            }
        }

        private void AddShadow() {
            DeviceContext.OutputMerger.BlendState = _blendState;
            DeviceContext.Rasterizer.State = null;
            DeviceContext.Rasterizer.SetViewports(Viewport);

            DeviceContext.OutputMerger.SetTargets(_summBuffer.TargetView);
            DeviceContextHolder.PrepareQuad(_effect.LayoutPT);

            _effect.FxDepthMap.SetResource(_shadowBuffer.View);
            _effect.FxShadowViewProj.SetMatrix(_shadowDestinationTransform * _shadowCamera.ViewProj * new Matrix {
                M11 = 0.5f,
                M22 = -0.5f,
                M33 = 1.0f,
                M41 = 0.5f,
                M42 = 0.5f,
                M44 = 1.0f
            });
            _effect.TechAmbientShadow.DrawAllPasses(DeviceContext, 6);
        }

        private void Draw(float multipler, int size, int padding, float fadeRadius) {
            DeviceContext.ClearRenderTargetView(_summBuffer.TargetView, Color.Transparent);

            /*var h = (int)Math.Round(Math.Pow(Iterations, 0.46));
            var v = (int)Math.Round(Math.Pow(Iterations, 0.54));
            var t = h * v;*/

            var t = Iterations;

            // draw
            var iter = 0f;
            for (var k = 0; k < t; k++) {
                if (DebugMode) {
                    DrawShadow(Vector3.UnitY, Vector3.UnitZ);
                } else {
                    /* arranged symmetric version */
                    /* var diff = DiffusionLevel.Saturate();

                     var φdeg = 360f * (k % h) / h;
                     var θdeg = 2f + 70f * ((float)Math.Floor((double)k / h) / (v - 1));
                     θdeg = (θdeg * diff + (1f - diff) * 89.9f).Clamp(5f, 89.9f);

                     var θ = (90f - θdeg).ToRadians();
                     var φ = φdeg.ToRadians();

                     var sinθ = θ.Sin();
                     var cosθ = θ.Cos();
                     var sinφ = φ.Sin();
                     var cosφ = φ.Cos();

                     DrawShadow(new Vector3(sinθ * cosφ, cosθ, sinθ * sinφ));*/

                    /* random distribution */

                    var v3 = default(Vector3);
                    do {
                        var x = MathF.Random(-1f, 1f);
                        var y = MathF.Random(0.1f, 1f) / DiffusionLevel.Clamp(0.001f, 1.0f);
                        var z = MathF.Random(-1f, 1f);
                        if (x.Abs() < 0.01 && z.Abs() < 0.01) continue;

                        v3 = new Vector3(x, y, z);
                    } while (v3.LengthSquared() > 1f);

                    DrawShadow(v3);
                }

                AddShadow();
                iter++;
            }

            DeviceContextHolder.PrepareQuad(_effect.LayoutPT);
            DeviceContext.Rasterizer.State = null;
            DeviceContext.OutputMerger.BlendState = null;
            DeviceContext.Rasterizer.SetViewports(Viewport);

            _effect.FxSize.Set(new Vector4(Width, Height, 1f / Width, 1f / Height));

            // blurring
            for (var i = BlurResult ? 2 : 1; i > 0; i--) {
                _effect.FxMultipler.Set(i > 1 ? 2f : 1f);

                DeviceContext.ClearRenderTargetView(_tempBuffer.TargetView, Color.Transparent);
                DeviceContext.OutputMerger.SetTargets(_tempBuffer.TargetView);

                _effect.FxInputMap.SetResource(_summBuffer.View);
                _effect.TechHorizontalShadowBlur.DrawAllPasses(DeviceContext, 6);

                DeviceContext.ClearRenderTargetView(_summBuffer.TargetView, Color.Transparent);
                DeviceContext.OutputMerger.SetTargets(_summBuffer.TargetView);

                _effect.FxInputMap.SetResource(_tempBuffer.View);
                _effect.TechVerticalShadowBlur.DrawAllPasses(DeviceContext, 6);
            }

            // result
            DeviceContext.ClearRenderTargetView(RenderTargetView, Color.Transparent);
            DeviceContext.OutputMerger.SetTargets(RenderTargetView);
            _effect.FxInputMap.SetResource(_summBuffer.View);
            _effect.FxCount.Set(iter / SkyBrightnessLevel);
            _effect.FxMultipler.Set(multipler);
            _effect.FxFade.Set(fadeRadius != 0f ? 10f / fadeRadius : 100f);
            _effect.FxPadding.Set(padding / (size + padding * 2f));
            _effect.FxShadowSize.Set(new Vector2(_shadowSize.X, _shadowSize.Z));
            _effect.TechResult.DrawAllPasses(DeviceContext, 6);
        }

        private void SaveResultAs(string outputDirectory, string name, int size, int padding) {
            using (var stream = new MemoryStream()) {
                Texture2D.ToStream(DeviceContext, RenderBuffer, ImageFileFormat.Png, stream);
                stream.Position = 0;

                using (var image = Image.FromStream(stream))
                using (var target = new Bitmap(size, size))
                using (var g = Graphics.FromImage(target)) {
                    var cropRect = new Rectangle(padding, padding, size, size);
                    g.DrawImage(image, new Rectangle(0, 0, target.Width, target.Height),
                            cropRect, GraphicsUnit.Pixel);
                    target.Save(Path.Combine(outputDirectory, name));
                }
            }
        }

        private void SetBodyShadowCamera() {
            _shadowSize = _ambientBodyShadowSize * (1f + 2f * BodyPadding / BodySize);
            var size = Math.Max(_shadowSize.X, _shadowSize.Z) * 2f;
            _shadowCamera = new CameraOrtho {
                Width = size,
                Height = size,
                NearZ = 0.001f,
                FarZ = size + 20f,
                DisableFrustum = true
            };
            _shadowCamera.SetLens(1f);
            _shadowDestinationTransform = Matrix.Scaling(new Vector3(-_shadowSize.X, _shadowSize.Y, _shadowSize.Z)) * Matrix.RotationY(MathF.PI);
        }

        private void SetWheelShadowCamera() {
            _shadowSize = _carData.GetWheelShadowSize() * (1f + 2f * WheelPadding / WheelSize);
            var size = Math.Max(_shadowSize.X, _shadowSize.Z) * 2f;
            _shadowCamera = new CameraOrtho {
                Width = size,
                Height = size,
                NearZ = 0.001f,
                FarZ = size + 20f,
                DisableFrustum = true
            };
            _shadowCamera.SetLens(1f);
            _shadowDestinationTransform = Matrix.Scaling(new Vector3(-_shadowSize.X, _shadowSize.Y, _shadowSize.Z)) * Matrix.RotationY(MathF.PI);
        }

        private void BackupAndRecycle(string outputDirectory) {
            var original = new[] {
                "body", "tyre_0", "tyre_1", "tyre_2", "tyre_3"
            }.Select(x => Path.Combine(outputDirectory, x + "_shadow.png")).Select(x => new {
                Original = x,
                Backup = x.ApartFromLast(".png") + "~bak.png"
            }).ToList();

            try {
                foreach (var p in original) {
                    if (File.Exists(p.Original)) {
                        File.Move(p.Original, p.Backup);
                    }
                }
            } catch (Exception e) {
                throw new Exception("Cannot remove original files", e);
            }

            Task.Run(() => {
                foreach (var p in original) {
                    FileUtils.Recycle(p.Backup);
                }
            });
        }

        public void Shot(string outputDirectory) {
            if (!Initialized) {
                Initialize();
            }

            BackupAndRecycle(outputDirectory);

            // body shadow
            PrepareBuffers(BodySize + BodyPadding * 2, 1024);
            SetBodyShadowCamera();
            Draw(BodyMultipler, BodySize, BodyPadding, Fade ? 0.5f : 0f);

            // return;
            SaveResultAs(outputDirectory, "body_shadow.png", BodySize, BodyPadding);

            // wheels shadows
            PrepareBuffers(WheelSize + WheelPadding * 2, 128);
            SetWheelShadowCamera();
            _wheelMode = true;

            var nodes = new[] { "WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR" };
            foreach (var entry in nodes.Select(x => _carNode.GetDummyByName(x)).NonNull().Select((x, i) => new {
                Node = x,
                Matrix = Matrix.Translation(0f, x.Matrix.GetTranslationVector().Y - (x.BoundingBox?.Minimum.Y ?? 0f), 0f),
                Filename = $"tyre_{i}_shadow.png"
            })) {
                _scene.Clear();
                _flattenNodes = null;

                _scene.Add(entry.Node);
                entry.Node.LocalMatrix = entry.Matrix;
                _scene.UpdateBoundingBox();

                Draw(WheelMultipler, WheelSize, WheelPadding, 1f);
                SaveResultAs(outputDirectory, entry.Filename, WheelSize, WheelPadding);
            }
        }

        public void Shot() {
            Shot(Path.GetDirectoryName(_kn5.OriginalFilename));
        }

        protected override void OnTick(float dt) { }

        protected override void DisposeOverride() {
            DisposeHelper.Dispose(ref _blendState);
            DisposeHelper.Dispose(ref _summBuffer);
            DisposeHelper.Dispose(ref _tempBuffer);
            DisposeHelper.Dispose(ref _shadowBuffer);
            _carNode.Dispose();
            _scene.Dispose();
            base.DisposeOverride();
        }
    }
}
