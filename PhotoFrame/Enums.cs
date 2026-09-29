// Models/Enums.cs — v1.3.0 (build 68)
//
// Публичные enum'ы настроек, используемые Services/, Converters/ и XAML.
// Восстановлены по фактическим обращениям кода (b52–b67) после ресинхронизации
// Free-базы с публичным репозиторием: ранее определения отсутствовали в дереве,
// что давало CS0246 при сборке CI.

namespace PhotoFrame.Models
{
    /// <summary>Режим автоотключения рамки (Настройки → Система).</summary>
    public enum AutoOffMode
    {
        Disabled = 0,
        SmartUsage = 1,
        ManualSchedule = 2,
        SunsetToSunrise = 3
    }

    /// <summary>Тема интерфейса.</summary>
    public enum UiMode
    {
        Modern = 0,
        Aero7 = 1
    }

    /// <summary>Формат счётчика фото на рамке.</summary>
    public enum CounterFormat
    {
        Hidden = 0,
        PhotoOnly = 1,
        WithTotal = 2
    }

    /// <summary>Точность отображения геолокации на плитке/подписи.</summary>
    public enum LocationPrecision
    {
        City = 0,
        District = 1,
        Street = 2
    }

    /// <summary>Плотность подбора фото для живых плиток.</summary>
    public enum TilePhotoDistance
    {
        Close = 0,
        Balanced = 1,
        Far = 2
    }
}
