using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Shared
{
    internal static class RoleBrushes
    {
        private static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Instance = B("#E2F0D9");
        public static readonly SolidColorBrush Type = B("#FFF2CC");
        public static readonly SolidColorBrush ReadOnly = B("#F8D7DA");
        public static readonly SolidColorBrush NotApplicable = B("#EDEDED");
        public static readonly SolidColorBrush Context = B("#F5F5F5");
        public static readonly SolidColorBrush Edited = B("#D6E4FF");
        public static readonly SolidColorBrush Invalid = B("#FFB8B8");

        public static Brush For(CellRole role)
        {
            switch (role)
            {
                case CellRole.InstanceEditable: return Instance;
                case CellRole.TypeEditable: return Type;
                case CellRole.InstanceReadOnly:
                case CellRole.TypeReadOnly: return ReadOnly;
                case CellRole.NotApplicable: return NotApplicable;
                default: return Context;
            }
        }
    }

    /// <summary>CellRole → soft background brush.</summary>
    public sealed class RoleToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is CellRole r ? RoleBrushes.For(r) : Brushes.Transparent;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>GridCell → background (edited/invalid override the role colour).</summary>
    public sealed class CellBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is GridCell c)) return Brushes.Transparent;
            if (c.State == GridCellState.Invalid) return RoleBrushes.Invalid;
            if (c.State == GridCellState.Edited) return RoleBrushes.Edited;
            return RoleBrushes.For(c.Role);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is Visibility v && v == Visibility.Visible;
            return Invert ? !b : b;
        }
    }

    public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

namespace CausaFix.Addin.Shared
{
    public sealed class NullToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }
}
