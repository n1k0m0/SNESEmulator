using System.Globalization;
using System.Windows.Data;
using SNESEmulator.Management;

namespace SNESEmulatorManager;
internal sealed class GameGroupConverter(Func<IReadOnlyList<GameItem>> games) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => games().FirstOrDefault(g => g.Hash == (string)value)?.Title ?? "Nicht zugeordnet";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
