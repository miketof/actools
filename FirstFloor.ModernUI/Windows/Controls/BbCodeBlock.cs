﻿using System;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Windows.Controls.BbCode;
using FirstFloor.ModernUI.Windows.Navigation;
using JetBrains.Annotations;

namespace FirstFloor.ModernUI.Windows.Controls {
    /// <summary>
    /// A lighweight control for displaying small amounts of rich formatted BbCode content.
    /// </summary>
    [Localizable(false), ContentProperty(nameof(BbCode))]
    public class BbCodeBlock : PlaceholderTextBlock {
        [CanBeNull]
        public static IEmojiProvider OptionEmojiProvider;

        [CanBeNull]
        public static string OptionEmojiCacheDirectory;

        [CanBeNull]
        public static string OptionImageCacheDirectory;

        public static string Encode(string value) {
            return value?.Replace("[", "\\[");
        }

        public static string Decode(string value) {
            return value?.Replace("\\[", "[");
        }

        public static string EncodeAttribute(string value) {
            return value == null ? null : "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public static event EventHandler<BbCodeImageEventArgs> ImageClicked;

        internal static void OnImageClicked(BbCodeImageEventArgs args) {
            ImageClicked?.Invoke(null, args);
        }

        public static readonly DependencyProperty BbCodeProperty = DependencyProperty.Register(nameof(BbCode), typeof(string), typeof(BbCodeBlock),
                new PropertyMetadata(OnBbCodeChanged));

        public string BbCode {
            get => (string)GetValue(BbCodeProperty);
            set => SetValue(BbCodeProperty, value);
        }

        public static readonly ILinkNavigator DefaultLinkNavigator = new DefaultLinkNavigator();

        public static void AddLinkCommand(Uri key, ICommand value) {
            DefaultLinkNavigator.Commands.Add(key, value);
        }

        public static readonly DependencyProperty LinkNavigatorProperty = DependencyProperty.Register(nameof(LinkNavigator), typeof(ILinkNavigator),
                typeof(BbCodeBlock), new PropertyMetadata(DefaultLinkNavigator, OnLinkNavigatorChanged));

        [CanBeNull]
        public ILinkNavigator LinkNavigator {
            get => (ILinkNavigator)GetValue(LinkNavigatorProperty);
            set => SetValue(LinkNavigatorProperty, value);
        }

        public static readonly DependencyProperty EmojiSupportProperty = DependencyProperty.Register(nameof(EmojiSupport), typeof(EmojiSupport),
                typeof(BbCodeBlock), new PropertyMetadata(EmojiSupport.WithEmoji));

        public EmojiSupport EmojiSupport {
            get => (EmojiSupport)GetValue(EmojiSupportProperty);
            set => SetValue(EmojiSupportProperty, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BbCodeBlock"/> class.
        /// </summary>
        public BbCodeBlock() {
            // ensures the implicit BbCodeBlock style is used
            DefaultStyleKey = typeof(BbCodeBlock);

            AddHandler(FrameworkContentElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));
        }

        private static void OnBbCodeChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
            ((BbCodeBlock)o).UpdateDirty();
        }

        private static void OnLinkNavigatorChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
            ((BbCodeBlock)o).UpdateDirty();
        }

        private bool _dirty;

        private void OnLoaded(object o, EventArgs e) {
            Update();
        }

        private void UpdateDirty() {
            _dirty = true;
            Update();
        }

        private static string EmojiToNumber(string emoji) {
            var result = new StringBuilder();

            for (var i = 0; i < emoji.Length; i++) {
                int code;
                if (char.IsHighSurrogate(emoji, i)) {
                    code = char.ConvertToUtf32(emoji, i);
                    i++;
                } else {
                    code = emoji[i];
                }

                if (code == 0x200d || code == 0xfe0f) {
                    continue;
                }

                if (result.Length > 0) {
                    result.Append('-');
                }

                result.Append(code.ToString("x"));
            }

            return result.ToString();
        }

        public static Inline ParseEmoji(string bbCode, bool allowBbCodes, FrameworkElement element = null, ILinkNavigator navigator = null) {
            var converted = new StringBuilder();
            var lastIndex = 0;
            var complex = false;

            for (var i = 0; i < bbCode.Length; i++) {
                var c = bbCode[i];

                if (c == '[') {
                    if (allowBbCodes) {
                        complex = true;
                    } else {
                        if (lastIndex != i) {
                            converted.Append(bbCode.Substring(lastIndex, i - lastIndex));
                        }
                        lastIndex = i + 1;
                        converted.Append(@"\[");
                        complex = true;
                    }
                    continue;
                }

                if (Emoji.IsEmoji(bbCode, i, out var length)) {
                    if (lastIndex != i) {
                        converted.Append(bbCode.Substring(lastIndex, i - lastIndex));
                    }

                    var emoji = bbCode.Substring(i, length);
                    converted.Append($"[img=\"emoji://{EmojiToNumber(emoji)}\"]{emoji}[/img]");
                    lastIndex = i + length;
                    complex = true;
                }

                // Even if it’s not an emoji, it still would be better to jump over high surrogates
                if (length > 1) {
                    i += length - 1;
                }
            }

            if (converted.Length > 0) {
                converted.Append(bbCode.Substring(lastIndex));
                bbCode = converted.ToString();
            }

            if (complex) {
                try {
                    return new BbCodeParser(bbCode, element) {
                        Commands = (navigator ?? DefaultLinkNavigator).Commands
                    }.Parse();
                } catch (Exception e) {
                    Logging.Error(e);
                }
            }

            return new Run { Text = bbCode };
        }

        private static Inline Parse(string bbCode, FrameworkElement element = null, ILinkNavigator navigator = null) {
            if (bbCode.IndexOf('[') != -1) {
                try {
                    return new BbCodeParser(bbCode, element) {
                        Commands = (navigator ?? DefaultLinkNavigator).Commands
                    }.Parse();
                } catch (Exception e) {
                    Logging.Error(e);
                }
            }

            return new Run { Text = bbCode };
        }

        private void Update() {
            if (!IsLoaded || !_dirty) return;

            var bbCode = BbCode;
            if (string.IsNullOrWhiteSpace(bbCode)) {
                SetPlaceholder();
            } else {
                var inlines = Inlines;
                var emojiSupport = EmojiSupport;
                inlines.Clear();
                inlines.Add(emojiSupport == EmojiSupport.NoEmoji
                        ? Parse(bbCode, this, LinkNavigator)
                        : ParseEmoji(bbCode, emojiSupport != EmojiSupport.NothingButEmoji, this, LinkNavigator));
            }

            _dirty = false;
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e) {
            try {
                // perform navigation using the link navigator
                LinkNavigator?.Navigate(e.Uri, this, e.Target);
            } catch (Exception ex) {
                // display navigation failures
                Logging.Warning(ex);
                ModernDialog.ShowMessage(ex.Message, UiStrings.NavigationFailed, MessageBoxButton.OK);
            }
        }
    }

    /// <summary>
    /// For copying.
    /// </summary>
    public class EmojiSpan : Span {
        public EmojiSpan() { }

        public EmojiSpan(string alt) {
            BaselineAlignment = BaselineAlignment.Center;
            Text = alt;
        }

        public string Text {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
                "Text", typeof(string), typeof(EmojiSpan), new PropertyMetadata("☺"));
    }

    /// <summary>
    /// Alternative version with selection support (totally different underneath).
    /// </summary>
    [Localizable(false), ContentProperty(nameof(BbCode))]
    public class SelectableBbCodeBlock : RichTextBox {
        /// <summary>
        /// Identifies the BbCode dependency property.
        /// </summary>
        public static readonly DependencyProperty BbCodeProperty = DependencyProperty.Register(nameof(BbCode), typeof(string), typeof(SelectableBbCodeBlock),
                new PropertyMetadata(OnBbCodeChanged));

        /// <summary>
        /// Identifies the LinkNavigator dependency property.
        /// </summary>
        public static readonly DependencyProperty LinkNavigatorProperty = DependencyProperty.Register(nameof(LinkNavigator), typeof(ILinkNavigator),
                typeof(SelectableBbCodeBlock), new PropertyMetadata(BbCodeBlock.DefaultLinkNavigator, OnLinkNavigatorChanged));

        private bool _dirty;

        /// <summary>
        /// Initializes a new instance of the <see cref="BbCodeBlock"/> class.
        /// </summary>
        public SelectableBbCodeBlock() {
            // ensures the implicit BbCodeBlock style is used
            DefaultStyleKey = typeof(SelectableBbCodeBlock);
            IsDocumentEnabled = true;

            AddHandler(FrameworkContentElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));
            DataObject.AddCopyingHandler(this, OnCopy);
        }

        private void OnCopy(object o, DataObjectCopyingEventArgs e) {
            var clipboard = "";

            for (TextPointer p = Selection.Start, next; p != null && p.CompareTo(Selection.End) < 0; p = next) {
                next = p.GetNextInsertionPosition(LogicalDirection.Forward);
                if (next == null) break;

                var textRange = new TextRange(p, next);
                clipboard += textRange.Start.Parent is EmojiSpan span ? span.Text : textRange.Text;
            }

            Clipboard.SetText(clipboard);
            e.Handled = true;
            e.CancelCommand();
        }

        private static void OnBbCodeChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
            ((SelectableBbCodeBlock)o).UpdateDirty();
        }

        private static void OnLinkNavigatorChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
            if (e.NewValue == null) {
                // null values disallowed
                throw new NullReferenceException("LinkNavigator");
            }

            ((SelectableBbCodeBlock)o).UpdateDirty();
        }

        private void OnLoaded(object o, EventArgs e) {
            Update();
        }

        private void UpdateDirty() {
            _dirty = true;
            Update();
        }

        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(nameof(LineHeight), typeof(double),
                typeof(SelectableBbCodeBlock), new PropertyMetadata(double.NaN, (o, e) => { ((SelectableBbCodeBlock)o)._lineHeight = (double)e.NewValue; }));

        private double _lineHeight = double.NaN;

        public double LineHeight {
            get => _lineHeight;
            set => SetValue(LineHeightProperty, value);
        }

        private void Update() {
            if (!IsLoaded || !_dirty) {
                return;
            }

            var bbCode = BbCode;

            Document.Blocks.Clear();
            if (!string.IsNullOrWhiteSpace(bbCode)) {
                var item = new Paragraph(BbCodeBlock.ParseEmoji(bbCode, true, this, LinkNavigator)) {
                    TextAlignment = TextAlignment.Left
                };

                if (!double.IsNaN(LineHeight)) {
                    item.LineHeight = LineHeight;
                }

                Document.Blocks.Add(item);
            }

            _dirty = false;
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e) {
            try {
                // perform navigation using the link navigator
                LinkNavigator.Navigate(e.Uri, this, e.Target);
            } catch (Exception ex) {
                // display navigation failures
                Logging.Warning(ex);
                ModernDialog.ShowMessage(ex.Message, UiStrings.NavigationFailed, MessageBoxButton.OK);
            }
        }

        /// <summary>
        /// Gets or sets the BB code.
        /// </summary>
        /// <value>The BB code.</value>
        public string BbCode {
            get => (string)GetValue(BbCodeProperty);
            set => SetValue(BbCodeProperty, value);
        }

        /// <summary>
        /// Gets or sets the link navigator.
        /// </summary>
        /// <value>The link navigator.</value>
        public ILinkNavigator LinkNavigator {
            get => (ILinkNavigator)GetValue(LinkNavigatorProperty);
            set => SetValue(LinkNavigatorProperty, value);
        }
    }
}