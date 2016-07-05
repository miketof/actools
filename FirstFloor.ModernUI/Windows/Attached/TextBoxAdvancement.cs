﻿using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FirstFloor.ModernUI.Windows.Attached {
    public static class TextBoxAdvancement {
        // sorry about it, but I don’t think I can handle another library only for number parsing
        private static class FlexibleParser {
            private static Regex _parseDouble, _parseInt;

            public static bool TryParseDouble(string s, out double value) {
                if (_parseDouble == null) {
                    _parseDouble = new Regex(@"-? *\d+([\.,]\d*)?");
                }

                if (s != null) {
                    var match = _parseDouble.Match(s);
                    if (match.Success) {
                        return double.TryParse(match.Value.Replace(',', '.').Replace(" ", ""), NumberStyles.Any,
                                               CultureInfo.InvariantCulture, out value);
                    }
                }

                value = 0.0;
                return false;
            }

            public static bool TryParseInt(string s, out int value) {
                if (_parseInt == null) {
                    _parseInt = new Regex(@"-? *\d+");
                }

                if (s != null) {
                    var match = _parseInt.Match(s);
                    if (match.Success) {
                        return int.TryParse(match.Value.Replace(" ", ""), NumberStyles.Any,
                                            CultureInfo.InvariantCulture, out value);
                    }
                }

                value = 0;
                return false;
            }

            public static string ReplaceDouble(string s, double value) {
                if (_parseDouble == null) {
                    _parseDouble = new Regex(@"-? *\d+([\.,]\d*)?");
                }

                var match = _parseDouble.Match(s);
                if (!match.Success) return s;

                return s.Substring(0, match.Index) + value.ToString(CultureInfo.InvariantCulture) +
                       s.Substring(match.Index + match.Length);
            }
        }

        public enum SpecialMode {
            None, Number, Integer, IntegerOrZeroLabel, Positive, Time
        }

        public static SpecialMode GetSpecialMode(DependencyObject obj) {
            return (SpecialMode)obj.GetValue(SpecialModeProperty);
        }

        public static void SetSpecialMode(DependencyObject obj, SpecialMode value) {
            obj.SetValue(SpecialModeProperty, value);
        }

        public static readonly DependencyProperty SpecialModeProperty = DependencyProperty.RegisterAttached("SpecialMode",
            typeof(SpecialMode), typeof(TextBoxAdvancement), new UIPropertyMetadata(OnSpecialModePropertyChanged));

        static void OnSpecialModePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var element = d as TextBox;
            if (element == null) return;

            if (e.NewValue is SpecialMode && (SpecialMode)e.NewValue != SpecialMode.None) {
                element.PreviewKeyDown += Element_KeyDown;
            } else {
                element.PreviewKeyDown -= Element_KeyDown;
            }
        }

        public static double GetMinValue(DependencyObject obj) {
            return (double)obj.GetValue(MinValueProperty);
        }

        public static void SetMinValue(DependencyObject obj, double value) {
            obj.SetValue(MinValueProperty, value);
        }

        public static readonly DependencyProperty MinValueProperty = DependencyProperty.RegisterAttached("MinValue", typeof(double),
                typeof(TextBoxAdvancement), new UIPropertyMetadata(double.MinValue));

        public static double GetMaxValue(DependencyObject obj) {
            return (double)obj.GetValue(MaxValueProperty);
        }

        public static void SetMaxValue(DependencyObject obj, double value) {
            obj.SetValue(MaxValueProperty, value);
        }

        public static readonly DependencyProperty MaxValueProperty = DependencyProperty.RegisterAttached("MaxValue", typeof(double),
                typeof(TextBoxAdvancement), new UIPropertyMetadata(double.MaxValue));

        private static void SetTextBoxText(TextBox textBox, string text) {
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            textBox.Text = text;
            textBox.SelectionStart = selectionStart;
            textBox.SelectionLength = selectionLength;
        }

        private static string ProcessText(SpecialMode mode, string text, double delta, double minValue, double maxValue) {
            switch (mode) {
                case SpecialMode.Number: {
                    double value;
                    if (!FlexibleParser.TryParseDouble(text, out value)) return null;

                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                        delta *= 0.1;
                    }

                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) {
                        delta *= 10.0;
                    }

                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) {
                        delta *= 2.0;
                    }

                    value += delta;
                    value = Math.Max(Math.Min(value, maxValue), minValue);
                    return FlexibleParser.ReplaceDouble(text, value);
                }

                case SpecialMode.Integer:
                case SpecialMode.Positive: {
                    int value;
                    if (!FlexibleParser.TryParseInt(text, out value)) return null;

                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) {
                        delta *= 10.0;
                    }

                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) {
                        delta *= 2.0;
                    }

                    value = (int)(value + delta);
                    if (mode == SpecialMode.Positive && value < 1) {
                        value = 1;
                    }

                    value = (int)Math.Max(Math.Min(value, maxValue), minValue);
                    return FlexibleParser.ReplaceDouble(text, value);
                }

                case SpecialMode.IntegerOrZeroLabel: {
                    int value;
                    var skip = !FlexibleParser.TryParseInt(text, out value);

                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) {
                        delta *= 10.0;
                    }

                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) {
                        delta *= 2.0;
                    }

                    value = (int)(value + delta);
                    if (mode == SpecialMode.Positive && value < 1) {
                        value = 1;
                    }

                    value = (int)Math.Max(Math.Min(value, maxValue), minValue);
                    return skip ? value.ToString(CultureInfo.InvariantCulture) : FlexibleParser.ReplaceDouble(text, value);
                }

                case SpecialMode.Time: {
                    var splitted = text.Split(':');
                    int hours, minutes;

                    if (splitted.Length != 2 || !FlexibleParser.TryParseInt(splitted[0], out hours) ||
                            !FlexibleParser.TryParseInt(splitted[1], out minutes)) return null;

                    var totalMinutes = hours * 60 + minutes;

                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                        delta *= 0.5;
                    }

                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) {
                        delta *= 6.0;
                    }

                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) {
                        delta *= 2.0;
                    }

                    totalMinutes += (int)(delta * 10);
                    totalMinutes = (int)Math.Max(Math.Min(totalMinutes, maxValue), minValue);
                    return $"{totalMinutes / 60:D}:{totalMinutes % 60:D}";
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static void Element_KeyDown(object sender, KeyEventArgs e) {
            if (!e.Key.Equals(Key.Up) && !e.Key.Equals(Key.Down)) return;

            var element = sender as TextBox;
            if (element == null) return;
            
            var processed = ProcessText(GetSpecialMode(element), element.Text, e.Key == Key.Up ? 1d : -1d, GetMinValue(element), GetMaxValue(element));
            if (processed != null) {
                SetTextBoxText(element, processed);
            }

            e.Handled = true;
        }
    }
}
