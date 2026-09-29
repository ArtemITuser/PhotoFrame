// Services/PlaylistManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public class PlaylistManager
    {
        private readonly List<PhotoInfo> _source = new();
        private List<int> _order = new();
        private int _pos = -1;
        private readonly Random _rng = new();
        private readonly HashSet<int> _trueRandomVisited = new();

        public void SetPhotos(IEnumerable<PhotoInfo> photos, PlayMode mode)
        {
            _source.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var photo in photos)
            {
                if (!string.IsNullOrWhiteSpace(photo.FilePath) && seen.Add(photo.FilePath))
                    _source.Add(photo);
            }
            Rebuild(mode);
        }

        public void Rebuild(PlayMode mode)
        {
            if (_source.Count == 0)
            {
                _order.Clear();
                _pos = -1;
                return;
            }

            _order = Enumerable.Range(0, _source.Count).ToList();

            _trueRandomVisited.Clear();

            switch (mode)
            {
                case PlayMode.Sequential:
                    _order.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(_source[a].FilePath, _source[b].FilePath));
                    break;
                case PlayMode.DateAscending:
                    _order = _order.OrderBy(i => _source[i].SortDate)
                                   .ThenBy(i => _source[i].FilePath, StringComparer.OrdinalIgnoreCase)
                                   .ToList();
                    break;
                case PlayMode.DateDescending:
                    _order = _order.OrderByDescending(i => _source[i].SortDate)
                                   .ThenBy(i => _source[i].FilePath, StringComparer.OrdinalIgnoreCase)
                                   .ToList();
                    break;
                case PlayMode.Shuffle:
                    FisherYates(_order);
                    break;
                case PlayMode.TrueRandom:
                    // Для TrueRandom порядок хранит все исходные индексы, а
                    // конкретный следующий кадр выбирается случайно.
                    break;
            }

            _pos = 0;
            if (mode == PlayMode.TrueRandom && _pos >= 0)
            {
                _pos = _rng.Next(_order.Count);
                _trueRandomVisited.Add(_order[_pos]);
            }
        }

        private void FisherYates(List<int> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        public int Count => _source.Count;
        public int CurrentIndex => (_pos >= 0 && _pos < _order.Count) ? _order[_pos] : 0;
        public int PlaybackPosition => _pos;
        public bool IsAtStart => _pos <= 0;
        public bool IsAtEnd => _order.Count == 0 ? true : _pos >= _order.Count - 1;
        public PhotoInfo? Current =>
            _source.Count > 0 && _pos >= 0 && _pos < _order.Count
                ? _source[_order[_pos]]
                : null;

        public PhotoInfo? Next(PlayMode mode, bool loop)
        {
            if (_source.Count == 0) return null;

            if (mode == PlayMode.TrueRandom)
            {
                if (!loop)
                {
                    if (_trueRandomVisited.Count >= _source.Count) return null;
                    var remaining = _order.Where(i => !_trueRandomVisited.Contains(i)).ToList();
                    int sourceIndex = remaining[_rng.Next(remaining.Count)];
                    _pos = _order.IndexOf(sourceIndex);
                    _trueRandomVisited.Add(sourceIndex);
                    return _source[sourceIndex];
                }

                int old = CurrentIndex;
                int next = _rng.Next(_order.Count);
                if (_order.Count > 1)
                {
                    while (_order[next] == old) next = _rng.Next(_order.Count);
                }
                _pos = next;
                return _source[_order[_pos]];
            }

            int nextPos = _pos + 1;
            if (nextPos >= _order.Count)
            {
                if (!loop) return null;

                int oldFirst = _order.Count > 0 ? _order[0] : -1;
                if (mode == PlayMode.Shuffle)
                {
                    FisherYates(_order);
                    if (_order.Count > 1 && _order[0] == oldFirst)
                        (_order[0], _order[1]) = (_order[1], _order[0]);
                }
                nextPos = 0;
            }

            _pos = nextPos;
            return _source[_order[_pos]];
        }

        public PhotoInfo? Prev(bool loop)
        {
            if (_source.Count == 0) return null;
            int prev = _pos - 1;
            if (prev < 0)
            {
                if (!loop) return _source[_order[0]];
                prev = _order.Count - 1;
            }
            _pos = prev;
            return _source[_order[_pos]];
        }

        public void JumpToSource(int sourceIndex)
        {
            int posInOrder = _order.IndexOf(sourceIndex);
            if (posInOrder >= 0) _pos = posInOrder;
        }

        public IEnumerable<PhotoInfo> GetOrderedPhotos() => _order.Select(i => _source[i]);
    }
}
