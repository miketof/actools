using System;
using System.Globalization;
using AcTools.Utils.Helpers;
using StringBasedFilter;
using StringBasedFilter.TestEntries;

namespace AcManager.Tools.Filters.TestEntries {
    public class DateTimeTestEntry : ITestEntry, ITestEntryFactory {
        public static readonly ITestEntryFactory Factory = new DateTimeTestEntry();

        ITestEntry ITestEntryFactory.Create(Operator op, string value) {
            try {
                var date = DateTime.Parse(value);
                return new DateTimeTestEntry(op, date);
            } catch (FormatException) {
                return null;
            }
        }

        // For factory
        private DateTimeTestEntry() { }

        private readonly Operator _op;
        private readonly DateTime _value;
        private readonly bool _exact;

        // For test entry
        private DateTimeTestEntry(Operator op, DateTime value) {
            _op = op;
            _value = value;
            _exact = true;
        }

        public override string ToString() {
            return _op.OperatorToString() + _value.ToString(CultureInfo.InvariantCulture);
        }

        public void Set(ITestEntryFactory factory) {}

        public bool Test(string value) {
            if (value == null) return false;
            return FlexibleParser.TryParseInt(value, out var val) && Test(val);
        }

        // days ago
        public bool Test(double value) {
            return Test(DateTime.Now - TimeSpan.FromDays(value));
        }

        public bool Test(bool value) {
            return Test(value ? 1.0 : 0.0);
        }

        public bool Test(TimeSpan value) {
            return Test(DateTime.Now.Date + value);
        }

        public bool Test(DateTime value) {
            var delta = _exact ? (long)Math.Floor((value - _value).TotalMinutes) : CompareByDays(value, _value);
            switch (_op) {
                case Operator.Less:
                    return delta < 0;

                case Operator.More:
                    return delta > 0;

                case Operator.Equal:
                    return delta == 0;

                case Operator.LessEqual:
                    return delta <= 0;

                case Operator.MoreEqual:
                    return delta >= 0;

                default:
                    return false;
            }
        }

        private static int CompareByDays(DateTime a, DateTime b) {
            var d = a.Year - b.Year;
            if (d != 0) return d;

            d = a.Month - b.Month;
            if (d != 0) return d;

            d = a.Day - b.Day;
            return d;
        }
    }
}