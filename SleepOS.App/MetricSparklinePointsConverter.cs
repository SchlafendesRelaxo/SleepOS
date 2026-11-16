using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SleepOS.App;

public sealed class MetricSparklinePointsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<double> values)
        {
            return new PointCollection();
        }

        var samples = values.TakeLast(60).ToArray();
        if (samples.Length < 2)
        {
            return new PointCollection();
        }

        const double width = 100;
        const double height = 28;
        var minimum = samples.Min();
        var range = Math.Max(1, samples.Max() - minimum);
        var points = new PointCollection(samples.Length);
        for (var index = 0; index < samples.Length; index++)
        {
            var x = width * index / (samples.Length - 1);
            var y = height - ((samples[index] - minimum) / range * (height - 4)) - 2;
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
