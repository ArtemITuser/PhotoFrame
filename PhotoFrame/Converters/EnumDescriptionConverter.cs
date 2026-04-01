// Converters/EnumDescriptionConverter.cs — конвертеры enum → русские строки
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using PhotoFrame.Models;

namespace PhotoFrame.Converters
{
    [ValueConversion(typeof(TransitionType), typeof(string))]
    public class TransitionTypeToStringConverter : IValueConverter
    {
        private static readonly Dictionary<TransitionType, string> Names = new()
        {
            { TransitionType.Random,       "Случайный"          },
            { TransitionType.Fade,         "Затухание"          },
            { TransitionType.SlideLeft,    "Сдвиг влево"        },
            { TransitionType.SlideRight,   "Сдвиг вправо"       },
            { TransitionType.SlideUp,      "Сдвиг вверх"        },
            { TransitionType.SlideDown,    "Сдвиг вниз"         },
            { TransitionType.ZoomIn,       "Приближение"        },
            { TransitionType.ZoomOut,      "Отдаление"          },
            { TransitionType.FlipH,        "Переворот по X"     },
            { TransitionType.FlipV,        "Переворот по Y"     },
            { TransitionType.BlurDissolve, "Блюр-растворение"   },
            { TransitionType.WipeLeft,     "Шторка влево"       },
            { TransitionType.WipeRight,    "Шторка вправо"      },
            { TransitionType.WipeUp,       "Шторка вверх"       },
            { TransitionType.WipeDown,     "Шторка вниз"        },
            { TransitionType.Checkerboard, "Шахматная доска"    },
            { TransitionType.KenBurns,     "Эффект Кен Бёрнса"  },
            { TransitionType.Mosaic,       "Мозаика"            },
            { TransitionType.Spiral,       "Спираль"            },
            { TransitionType.PageTurn,     "Листание страницы"  },
        };
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is TransitionType tt && Names.TryGetValue(tt, out var n) ? n : v?.ToString() ?? "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    [ValueConversion(typeof(AppTheme), typeof(string))]
    public class AppThemeToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            AppTheme.System => "Системная",
            AppTheme.Dark   => "Тёмная",
            AppTheme.Light  => "Светлая",
            _               => v?.ToString() ?? ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    [ValueConversion(typeof(PlayMode), typeof(string))]
    public class PlayModeToStringConverter : IValueConverter
    {
        private static readonly Dictionary<PlayMode, string> Names = new()
        {
            { PlayMode.Sequential,     "По порядку (имя файла)"   },
            { PlayMode.Shuffle,        "Перемешать (без повторов)" },
            { PlayMode.TrueRandom,     "Случайно (с повторами)"   },
            { PlayMode.DateAscending,  "По дате: старые → новые"  },
            { PlayMode.DateDescending, "По дате: новые → старые"  },
        };
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is PlayMode pm && Names.TryGetValue(pm, out var n) ? n : v?.ToString() ?? "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
