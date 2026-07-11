// Services/PlaylistManager.cs
// Управляет порядком воспроизведения фотографий.
// Поддерживает: Sequential, Shuffle (без повторов), TrueRandom, DateAscending, DateDescending.

using System;
using System.Collections.Generic;
using System.Linq;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public class PlaylistManager
    {
        private readonly List<PhotoInfo> _source  = new();
        private List<int>                _order   = new();
        private int                      _pos     = -1;
        private readonly Random          _rng     = new();

        // ─── Инициализация ────────────────────────────────────────────────────────

        /// <summary>Заменяет исходный список фотографий и перестраивает очередь.</summary>
        public void SetPhotos(IEnumerable<PhotoInfo> photos, PlayMode mode)
        {
            _source.Clear();
            _source.AddRange(photos);
            Rebuild(mode);
        }

        /// <summary>Перестраивает очередь воспроизведения под новый режим.</summary>
        public void Rebuild(PlayMode mode)
        {
            if (_source.Count == 0) { _order.Clear(); _pos = -1; return; }

            switch (mode)
            {
                case PlayMode.Sequential:
                    _order = Enumerable.Range(0, _source.Count)
                        .OrderBy(i => _source[i].FilePath)
                        .ToList();
                    break;

                case PlayMode.DateAscending:
                    _order = Enumerable.Range(0, _source.Count)
                        .OrderBy(i => _source[i].DateTaken ?? DateTime.MaxValue)
                        .ThenBy(i => _source[i].FilePath)
                        .ToList();
                    break;

                case PlayMode.DateDescending:
                    _order = Enumerable.Range(0, _source.Count)
                        .OrderByDescending(i => _source[i].DateTaken ?? DateTime.MinValue)
                        .ThenBy(i => _source[i].FilePath)
                        .ToList();
                    break;

                case PlayMode.Shuffle:
                    _order = Enumerable.Range(0, _source.Count).ToList();
                    // Fisher-Yates
                    for (int i = _order.Count - 1; i > 0; i--)
                    {
                        int j = _rng.Next(i + 1);
                        (_order[i], _order[j]) = (_order[j], _order[i]);
                    }
                    break;

                case PlayMode.TrueRandom:
                    // Порядок строится случайно; при нехватке — перестраивается
                    _order = Enumerable.Range(0, _source.Count).ToList();
                    break;
            }

            _pos = _order.Count > 0 ? 0 : -1;
        }

        // ─── Навигация ────────────────────────────────────────────────────────────

        public int Count => _source.Count;

        /// <summary>Индекс текущего фото в ИСХОДНОМ списке _source (не путать с
        /// позицией в очереди воспроизведения!). Используется для JumpToSource.</summary>
        public int CurrentIndex => (_pos >= 0 && _pos < _order.Count) ? _order[_pos] : 0;

        /// <summary>
        /// Текущая ПОЗИЦИЯ в очереди воспроизведения (0-based) — то, что
        /// нужно показывать в счётчике "N / всего", и для определения
        /// начала/конца списка в Shuffle/Random режимах, где CurrentIndex
        /// (индекс в исходном списке) НЕ совпадает с порядком показа.
        /// </summary>
        public int PlaybackPosition => _pos;

        /// <summary>True, если сейчас показывается первое фото в очереди воспроизведения.</summary>
        public bool IsAtStart => _pos <= 0;

        /// <summary>True, если сейчас показывается последнее фото в очереди воспроизведения.</summary>
        public bool IsAtEnd => _pos >= _order.Count - 1;

        /// <summary>Текущий объект PhotoInfo или null.</summary>
        public PhotoInfo? Current =>
            (_source.Count > 0 && _pos >= 0 && _pos < _order.Count)
                ? _source[_order[_pos]]
                : null;

        /// <summary>Переходит к следующему кадру. При TrueRandom — случайный каждый раз.</summary>
        public PhotoInfo? Next(PlayMode mode, bool loop)
        {
            if (_source.Count == 0) return null;

            if (mode == PlayMode.TrueRandom)
            {
                _pos = _rng.Next(_source.Count);
                // Убираем совпадение с предыдущим при count > 1
                if (_source.Count > 1)
                {
                    int old = CurrentIndex;
                    while (_order[_pos] == old)
                        _pos = _rng.Next(_source.Count);
                }
                return _source[_order[_pos]];
            }

            int next = _pos + 1;

            if (next >= _order.Count)
            {
                if (!loop) return null;

                // Перетасовываем заново при Shuffle-loop, чтобы первый != последний
                if (mode == PlayMode.Shuffle)
                {
                    int lastIdx = _order[_pos];
                    Rebuild(mode);
                    // Гарантируем что первый != последний из прошлого цикла
                    if (_order.Count > 1 && _order[0] == lastIdx)
                    {
                        int swap = _rng.Next(1, _order.Count);
                        (_order[0], _order[swap]) = (_order[swap], _order[0]);
                    }
                }
                next = 0;
            }

            _pos = next;
            return _source[_order[_pos]];
        }

        /// <summary>Переходит к предыдущему кадру.</summary>
        public PhotoInfo? Prev(bool loop)
        {
            if (_source.Count == 0) return null;
            int prev = _pos - 1;
            if (prev < 0) prev = loop ? _order.Count - 1 : 0;
            _pos = prev;
            return _source[_order[_pos]];
        }

        /// <summary>Прыжок к конкретному индексу в _source.</summary>
        public void JumpToSource(int sourceIndex)
        {
            int posInOrder = _order.IndexOf(sourceIndex);
            if (posInOrder >= 0) _pos = posInOrder;
        }

        /// <summary>Возвращает список PhotoInfo в текущем порядке воспроизведения (для превью).</summary>
        public IEnumerable<PhotoInfo> GetOrderedPhotos()
            => _order.Select(i => _source[i]);
    }
}
