﻿using System;
using System.IO;
using System.Threading.Tasks;
using AcTools.Utils.Helpers;
using JetBrains.Annotations;
using SlimDX.Direct3D11;
using Debug = System.Diagnostics.Debug;

namespace AcTools.Render.Kn5Specific.Textures {
    public class RenderableTexture : IRenderableTexture {
        public string Name { get; }

        public bool IsDisposed { get; private set; }

        public RenderableTexture(string name = null) {
            Name = name;
            Debug.WriteLine("[RenderableTexture] CREATED: " + name);
        }

        private ShaderResourceView _resource;

        public ShaderResourceView Resource {
            get { return _proceduralOverride ?? _override ?? _resource; }
            internal set {
                if (Equals(_resource, value)) return;
                DisposeHelper.Dispose(ref _resource);
                _resource = value;
            }
        }

        private ShaderResourceView _override;

        public ShaderResourceView Override {
            get { return _override; }
            internal set {
                if (Equals(_override, value)) return;
                DisposeHelper.Dispose(ref _override);
                _override = value;
            }
        }

        private ShaderResourceView _proceduralOverride;

        public ShaderResourceView ProceduralOverride {
            get { return _proceduralOverride; }
            set {
                if (Equals(value, _proceduralOverride)) return;
                DisposeHelper.Dispose(ref _proceduralOverride);
                _proceduralOverride = value;
            }
        }

        public void SetProceduralOverride(Device device, byte[] textureBytes) {
            if (textureBytes == null) {
                ProceduralOverride = null;
                return;
            }

            try {
                ProceduralOverride = ShaderResourceView.FromMemory(device, textureBytes);
            } catch (Exception) {
                ProceduralOverride = null;
            }
        }

        private int _resourceId, _overrideId;

        public void Load(Device device, string filename) {
            var id = ++_resourceId;
            var resource = ShaderResourceView.FromFile(device, filename);
            if (id != _resourceId) return;
            Resource = resource;
        }

        public void Load(Device device, byte[] data) {
            var id = ++_resourceId;
            var resource = ShaderResourceView.FromMemory(device, data);
            if (id != _resourceId) return;
            Resource = resource;
        }

        public async Task LoadAsync(Device device, string filename) {
            var id = ++_resourceId;
            var resource = await Task.Run(() => ShaderResourceView.FromFile(device, filename));
            if (id != _resourceId) return;
            Resource = resource;
        }

        public async Task LoadAsync(Device device, byte[] data) {
            var id = ++_resourceId;
            var resource = await Task.Run(() => ShaderResourceView.FromMemory(device, data));
            if (id != _resourceId) return;
            Resource = resource;
        }

        public async Task LoadOverrideAsync(Device device, string filename) {
            var id = ++_overrideId;

            if (filename == null || !File.Exists(filename)) {
                Override = null;
                return;
            }

            try {
                var resource = await Task.Run(() => ShaderResourceView.FromFile(device, filename));
                if (id != _overrideId) return;
                Override = resource;
            } catch (Exception) {
                if (id != _overrideId) return;
                Override = null;
            }
        }

        public void LoadOverride(Device device, byte[] data) {
            var id = ++_overrideId;

            if (data == null) {
                Override = null;
                return;
            }

            try {
                var resource = ShaderResourceView.FromMemory(device, data);
                if (id != _overrideId) return;
                Override = resource;
            } catch (Exception) {
                if (id != _overrideId) return;
                Override = null;
            }
        }

        public async Task LoadOverrideAsync(Device device, [CanBeNull] byte[] data) {
            var id = ++_overrideId;

            if (data == null) {
                Override = null;
                return;
            }

            try {
                var resource = await Task.Run(() => ShaderResourceView.FromMemory(device, data));
                if (id != _overrideId) return;
                Override = resource;
            } catch (Exception) {
                if (id != _overrideId) return;
                Override = null;
            }
        }

        public void Dispose() {
            DisposeHelper.Dispose(ref _override);
            DisposeHelper.Dispose(ref _resource);
            Debug.WriteLine("[RenderableTexture] DISPOSED: " + Name);
            IsDisposed = true;
        }
    }
}
