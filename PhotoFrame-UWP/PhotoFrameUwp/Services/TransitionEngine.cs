// Services/TransitionEngine.cs — UWP-порт движка анимаций (логика 1:1 с WPF v3.2)
// Управляет двумя слоями Image (A и B). После перехода слои меняются ролями.
//
// Соответствие API WPF → UWP (Windows.UI.Xaml):
//   System.Windows.Controls.Image            → Windows.UI.Xaml.Controls.Image
//   BitmapSource/BitmapImage                 → BitmapImage (Windows.UI.Xaml.Media.Imaging)
//   UIElement.OpacityProperty                → UIElement.OpacityProperty (есть)
//   TranslateTransform/ScaleTransform        → есть, свойства X/Y/ScaleX/ScaleY (double)
//   BlurEffect.RadiusProperty                → Effect = GaussianBlurEffect? НЕТ в 15063 →
//                                              имитация через Composition? Нет — используем
//                                              OpacityMask + Scale «расплывание» (см. DoBlurDissolve)
//   RectangleGeometry.RectProperty + RectAnimation → CompositeTransform не анимируется;
//                                              для Wipe используется Clip + DoubleAnimation
//                                              на Canvas-обёртке (эмуляция шторки)
//   DrawingBrush как OpacityMask             → ImageBrush из генерируемого PNG-паттерна
//                                              (checkerboard/mosaic) — см. EnsureMaskAsync
//   SkewTransform                            → CompositeTransform.SkewX (свойство double)
//   RenderTransformOrigin                    → есть (Point)
//   FillBehavior.Stop                        → есть
//   CubicEase/QuarticEase/SineEase           → есть

using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using PhotoFrameUwp.Models;

namespace PhotoFrameUwp.Services
{
    public class TransitionEngine
    {
        // ─── Поля ─────────────────────────────────────────────────────────────────
        private readonly Image            _imgA, _imgB;
        private readonly FrameworkElement _container;

        private bool _isFrontA        = true;
        private bool _isTransitioning = false;

        private readonly CompositeTransform _transA, _transB; // scale+translate+skew+rotate

        private static readonly Random _rng = new Random();

        private static readonly TransitionType[] _concretes =
        {
            TransitionType.Fade,
            TransitionType.SlideLeft, TransitionType.SlideRight,
            TransitionType.SlideUp,   TransitionType.SlideDown,
            TransitionType.ZoomIn,    TransitionType.ZoomOut,
            TransitionType.FlipH,     TransitionType.FlipV,
            TransitionType.BlurDissolve,
            TransitionType.WipeLeft,  TransitionType.WipeRight,
            TransitionType.WipeUp,    TransitionType.WipeDown,
            TransitionType.Checkerboard,
            TransitionType.KenBurns,
            TransitionType.Mosaic,
            TransitionType.Spiral,
            TransitionType.PageTurn,
        };

        // Кэш масок-паттернов (checkerboard/mosaic), генерируются один раз
        private WriteableBitmap _checkerMask, _mosaicMask;

        // ─── Свойства front/back ──────────────────────────────────────────────────
        private Image              Front      => _isFrontA ? _imgA : _imgB;
        private Image              Back       => _isFrontA ? _imgB : _imgA;
        private CompositeTransform FrontTrans => _isFrontA ? _transA : _transB;
        private CompositeTransform BackTrans  => _isFrontA ? _transB : _transA;

        public bool IsTransitioning => _isTransitioning;

        // ─── Конструктор ──────────────────────────────────────────────────────────
        public TransitionEngine(Image imgA, Image imgB, FrameworkElement container)
        {
            _imgA = imgA; _imgB = imgB; _container = container;

            _transA = new CompositeTransform();
            _imgA.RenderTransformOrigin = new Point(0.5, 0.5);
            _imgA.RenderTransform = _transA;

            _transB = new CompositeTransform();
            _imgB.RenderTransformOrigin = new Point(0.5, 0.5);
            _imgB.RenderTransform = _transB;
        }

        // ─── Публичный API ────────────────────────────────────────────────────────

        public void ShowImmediate(BitmapSource source)
        {
            _isTransitioning = false;
            Front.BeginAnimation(UIElement.OpacityProperty, null);
            Back.BeginAnimation(UIElement.OpacityProperty,  null);
            CleanupEffects();
            Front.Source  = source;
            Front.Opacity = 1;
            Back.Source   = null;
            Back.Opacity  = 0;
            ResetAll();
        }

        public void Transition(
            BitmapSource newSource,
            TransitionType type,
            double durationSec,
            Action onCompleted = null)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;

            var actual = (type == TransitionType.Random)
                ? _concretes[_rng.Next(_concretes.Length)]
                : type;

            var dur = TimeSpan.FromSeconds(Math.Max(0.15, durationSec));

            Back.Source  = newSource;
            Back.Opacity = 0;
            Back.OpacityMask = null;
            ResetBackTransforms();

            // Standard swap-based complete (Fade, Slide, Zoom, Blur, Wipe, Checker, Mosaic, Spiral)
            Action completeSwap = () =>
            {
                Front.BeginAnimation(UIElement.OpacityProperty, null);
                Back.BeginAnimation(UIElement.OpacityProperty,  null);
                CleanupEffects();
                Back.Opacity  = 1;
                Front.Opacity = 0;
                ResetAll();
                _isFrontA        = !_isFrontA;
                _isTransitioning = false;
                if (onCompleted != null) onCompleted();
            };

            // No-swap complete (Flip, PageTurn, KenBurns — manage layers themselves)
            Action completeNoSwap = () =>
            {
                CleanupEffects();
                ResetAll();
                _isTransitioning = false;
                if (onCompleted != null) onCompleted();
            };

            switch (actual)
            {
                case TransitionType.Fade:         DoFade(dur, completeSwap);                    break;
                case TransitionType.SlideLeft:    DoSlide(dur, completeSwap, -1,  0);           break;
                case TransitionType.SlideRight:   DoSlide(dur, completeSwap,  1,  0);           break;
                case TransitionType.SlideUp:      DoSlide(dur, completeSwap,  0, -1);           break;
                case TransitionType.SlideDown:    DoSlide(dur, completeSwap,  0,  1);           break;
                case TransitionType.ZoomIn:       DoZoom(dur, completeSwap, true);              break;
                case TransitionType.ZoomOut:      DoZoom(dur, completeSwap, false);             break;
                case TransitionType.FlipH:        DoFlip(dur, completeNoSwap, true);            break;
                case TransitionType.FlipV:        DoFlip(dur, completeNoSwap, false);           break;
                case TransitionType.BlurDissolve: DoBlurDissolve(dur, completeSwap);            break;
                case TransitionType.WipeLeft:     DoWipe(dur, completeSwap, Dir.Left);          break;
                case TransitionType.WipeRight:    DoWipe(dur, completeSwap, Dir.Right);         break;
                case TransitionType.WipeUp:       DoWipe(dur, completeSwap, Dir.Up);            break;
                case TransitionType.WipeDown:     DoWipe(dur, completeSwap, Dir.Down);          break;
                case TransitionType.Checkerboard: DoCheckerboard(dur, completeSwap);            break;
                case TransitionType.KenBurns:     DoKenBurns(dur, completeNoSwap);              break;
                case TransitionType.Mosaic:       DoMosaic(dur, completeSwap);                  break;
                case TransitionType.Spiral:       DoSpiral(dur, completeSwap);                  break;
                case TransitionType.PageTurn:     DoPageTurn(dur, completeNoSwap);              break;
                default:                          DoFade(dur, completeSwap);                    break;
            }
        }

        // ─── FADE ─────────────────────────────────────────────────────────────────
        private void DoFade(TimeSpan dur, Action done)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var i = Anim(0, 1, dur, ease); AttachDone(i, done);
            Back.BeginAnimation(UIElement.OpacityProperty,  i);
            Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, dur, ease));
        }

        // ─── SLIDE ────────────────────────────────────────────────────────────────
        private void DoSlide(TimeSpan dur, Action done, int dx, int dy)
        {
            var size = BoxSize; var ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            double ox = dx * size.Width, oy = dy * size.Height;
            BackTrans.TranslateX = -ox; BackTrans.TranslateY = -oy;
            Back.Opacity = 1;
            var inX = Anim(-ox, 0, dur, ease); AttachDone(inX, done);
            BackTrans.BeginAnimation(CompositeTransform.TranslateXProperty, inX);
            BackTrans.BeginAnimation(CompositeTransform.TranslateYProperty, Anim(-oy, 0, dur, ease));
            FrontTrans.BeginAnimation(CompositeTransform.TranslateXProperty, Anim(0, ox, dur, ease));
            FrontTrans.BeginAnimation(CompositeTransform.TranslateYProperty, Anim(0, oy, dur, ease));
        }

        // ─── ZOOM ─────────────────────────────────────────────────────────────────
        private void DoZoom(TimeSpan dur, Action done, bool zoomIn)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            if (zoomIn)
            {
                BackTrans.ScaleX = BackTrans.ScaleY = 0.3; Back.Opacity = 0;
                var fi = Anim(0, 1, dur, ease); AttachDone(fi, done);
                BackTrans.BeginAnimation(CompositeTransform.ScaleXProperty, Anim(0.3, 1, dur, ease));
                BackTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(0.3, 1, dur, ease));
                Back.BeginAnimation(UIElement.OpacityProperty,  fi);
                Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, dur, ease));
            }
            else
            {
                Back.Opacity = 1;
                var sx = Anim(1, 0.3, dur, ease); AttachDone(sx, done);
                FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, sx);
                FrontTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(1, 0.3, dur, ease));
                Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, dur, ease));
            }
        }

        // ─── FLIP (однослойный) ───────────────────────────────────────────────────
        private void DoFlip(TimeSpan dur, Action done, bool horizontal)
        {
            var half = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            var eIn  = new SineEase { EasingMode = EasingMode.EaseIn };
            var prop = horizontal ? CompositeTransform.ScaleXProperty
                                  : CompositeTransform.ScaleYProperty;
            var col = Anim(1, 0, half, eIn);
            col.Completed += (_, __) =>
            {
                Front.Source = Back.Source; Back.Source = null; Back.Opacity = 0;
                if (horizontal) FrontTrans.ScaleY = 1.0; else FrontTrans.ScaleX = 1.0;
                var exp = Anim(0, 1, half, new SineEase { EasingMode = EasingMode.EaseOut });
                exp.Completed += (_, __2) => { FrontTrans.ScaleX = FrontTrans.ScaleY = 1; done(); };
                var eprop = horizontal ? CompositeTransform.ScaleXProperty
                                       : CompositeTransform.ScaleYProperty;
                FrontTrans.BeginAnimation(eprop, exp);
            };
            FrontTrans.BeginAnimation(prop, col);
        }

        // ─── BLUR DISSOLVE ────────────────────────────────────────────────────────
        // GaussianBlurEffect нет в XAML-фильтрах 15063 → «растворение» имитируется
        // кросс-фейдом с одновременным масштабированием (soft dissolve).
        private void DoBlurDissolve(TimeSpan dur, Action done)
        {
            var half = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            var easeIn  = new CubicEase { EasingMode = EasingMode.EaseIn };
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            Back.Opacity = 0;
            BackTrans.ScaleX = BackTrans.ScaleY = 1.08;   // лёгкий «расфокус» масштабом

            var fo = Anim(1, 0, dur, easeIn);
            FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, Anim(1, 1.08, dur, easeIn));
            FrontTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(1, 1.08, dur, easeIn));

            var bi = Anim(0, 1, dur, easeOut); AttachDone(bi, done);
            BackTrans.BeginAnimation(CompositeTransform.ScaleXProperty, Anim(1.08, 1, dur, easeOut));
            BackTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(1.08, 1, dur, easeOut));

            Front.BeginAnimation(UIElement.OpacityProperty, fo);
            Back.BeginAnimation(UIElement.OpacityProperty,  bi);
        }

        // ─── WIPE ─────────────────────────────────────────────────────────────────
        // RectangleGeometry.RectProperty не анимируется напрямую в UWP →
        // шторка делается через ClipRectangle-подход: клип привязан к RectangleGeometry,
        // анимируем её Rect через ObjectAnimationsUsingKeyFrames? Проще и надёжнее на
        // ARM32 15254 — анимируем Translate/Scale маскированного слоя. Реализация:
        // Back закрыт OpacityMask = ImageBrush с градиентной полосой, которая двигается.
        private void DoWipe(TimeSpan dur, Action done, Dir dir)
        {
            var grad = new LinearGradientBrush
            {
                StartPoint = GradientStart(dir),
                EndPoint   = GradientEnd(dir),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(Colors.White, 0.5),
                    new GradientStop(Colors.Transparent, 0.5),
                    new GradientStop(Colors.Transparent, 1.0),
                },
                // сдвигаем маску анимацией RelativeTransform
                RelativeTransform = new CompositeTransform()
            };

            Back.OpacityMask = grad;
            Back.Opacity = 1;

            var maskT = (CompositeTransform)grad.RelativeTransform;
            // от полностью скрытого (-1 по оси) до полностью открытого (+1)
            double from = -1.0, to = 1.0;
            DoubleAnimation anim;
            if (dir == Dir.Left || dir == Dir.Right)
            {
                maskT.TranslateX = from;
                anim = Anim(from, to, dur, new QuarticEase { EasingMode = EasingMode.EaseInOut });
                AttachDone(anim, () => { Back.OpacityMask = null; done(); });
                maskT.BeginAnimation(CompositeTransform.TranslateXProperty, anim);
            }
            else
            {
                maskT.TranslateY = from;
                anim = Anim(from, to, dur, new QuarticEase { EasingMode = EasingMode.EaseInOut });
                AttachDone(anim, () => { Back.OpacityMask = null; done(); });
                maskT.BeginAnimation(CompositeTransform.TranslateYProperty, anim);
            }
        }

        private static Point GradientStart(Dir d)
        {
            switch (d)
            {
                case Dir.Left:  return new Point(0, 0);
                case Dir.Right: return new Point(1, 0);
                case Dir.Up:    return new Point(0, 0);
                default:        return new Point(0, 1);
            }
        }

        private static Point GradientEnd(Dir d)
        {
            switch (d)
            {
                case Dir.Left:  return new Point(1, 0);
                case Dir.Right: return new Point(0, 0);
                case Dir.Up:    return new Point(0, 1);
                default:        return new Point(0, 0);
            }
        }

        // ─── CHECKERBOARD ─────────────────────────────────────────────────────────
        // В UWP нет DrawingBrush → маска генерируется один раз как WriteableBitmap
        // (чёрно-белая шахматка 8×8) и используется как OpacityMask ImageBrush.
        private void DoCheckerboard(TimeSpan dur, Action done)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var mask = EnsurePatternBrush(8, true);
            Back.OpacityMask = mask;
            Back.Opacity     = 1;

            var fadeOut = Anim(1, 0, dur, ease);
            AttachDone(fadeOut, () => { Back.OpacityMask = null; done(); });
            Front.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // ─── KEN BURNS (zoom + pan, однослойный) ──────────────────────────────────
        private void DoKenBurns(TimeSpan dur, Action done)
        {
            Front.Source  = Back.Source;
            Back.Source   = null;
            Back.Opacity  = 0;
            Front.Opacity = 1;

            double startScale = 1.15;
            double offX = (_rng.NextDouble() - 0.5) * BoxSize.Width  * 0.1;
            double offY = (_rng.NextDouble() - 0.5) * BoxSize.Height * 0.1;

            FrontTrans.ScaleX = FrontTrans.ScaleY = startScale;
            FrontTrans.TranslateX = offX; FrontTrans.TranslateY = offY;

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var sx = Anim(startScale, 1, dur, ease); AttachDone(sx, done);
            FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, sx);
            FrontTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(startScale, 1, dur, ease));
            FrontTrans.BeginAnimation(CompositeTransform.TranslateXProperty, Anim(offX, 0, dur, ease));
            FrontTrans.BeginAnimation(CompositeTransform.TranslateYProperty, Anim(offY, 0, dur, ease));
        }

        // ─── MOSAIC ───────────────────────────────────────────────────────────────
        // Двухшаговый cross-dissolve с мозаичной OpacityMask (WriteableBitmap)
        private void DoMosaic(TimeSpan dur, Action done)
        {
            var ease  = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var half  = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            Back.Opacity = 0;

            FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, Anim(1, 1.3, half, ease));
            FrontTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(1, 1.3, half, ease));

            var fo = Anim(1, 0, half, ease);
            fo.Completed += (_, __) =>
            {
                Front.Opacity = 0;
                BackTrans.ScaleX = BackTrans.ScaleY = 1.3;
                Back.OpacityMask = EnsurePatternBrush(12, false);
                Back.Opacity = 1;
                BackTrans.BeginAnimation(CompositeTransform.ScaleXProperty, Anim(1.3, 1, half, ease));
                BackTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(1.3, 1, half, ease));

                var fi = Anim(0, 1, half, ease);
                fi.Completed += (_, __2) => { Back.OpacityMask = null; done(); };
                Back.BeginAnimation(UIElement.OpacityProperty, fi);
            };
            Front.BeginAnimation(UIElement.OpacityProperty, fo);
        }

        // ─── SPIRAL (rotate + fade) ───────────────────────────────────────────────
        private void DoSpiral(TimeSpan dur, Action done)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            Back.Opacity = 0;

            var rotAnim = new DoubleAnimation(0, 180, new Duration(dur))
                { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var fadeIn = Anim(0, 1, dur, ease);
            AttachDone(fadeIn, () =>
            {
                FrontTrans.Rotation = 0;
                done();
            });

            FrontTrans.BeginAnimation(CompositeTransform.RotationProperty, rotAnim);
            FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, Anim(1, 0, dur, ease));
            FrontTrans.BeginAnimation(CompositeTransform.ScaleYProperty, Anim(1, 0, dur, ease));
            Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, dur, ease));
            Back.BeginAnimation(UIElement.OpacityProperty,  fadeIn);
        }

        // ─── PAGE TURN (однослойный — skew + scale) ───────────────────────────────
        private void DoPageTurn(TimeSpan dur, Action done)
        {
            var half = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            var eIn  = new CubicEase { EasingMode = EasingMode.EaseIn };

            var skewAnim = new DoubleAnimation(0, -20, new Duration(half))
                { EasingFunction = eIn, FillBehavior = FillBehavior.Stop };
            var narrowX = Anim(1, 0, half, eIn);

            narrowX.Completed += (_, __) =>
            {
                FrontTrans.SkewX = 0;
                Front.Source = Back.Source; Back.Source = null;
                Front.Opacity = 1;
                FrontTrans.ScaleX = 0;
                var expandX = Anim(0, 1, half, new CubicEase { EasingMode = EasingMode.EaseOut });
                expandX.Completed += (_, __2) => done();
                FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, expandX);
            };

            FrontTrans.BeginAnimation(CompositeTransform.SkewXProperty, skewAnim);
            FrontTrans.BeginAnimation(CompositeTransform.ScaleXProperty, narrowX);
            Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, half, eIn));
        }

        // ─── Генератор паттерн-масок (checkerboard / mosaic) ──────────────────────
        // В UWP нет DrawingBrush → маска генерируется один раз как WriteableBitmap
        // и оборачивается в ImageBrush для OpacityMask.

        private static readonly System.Collections.Generic.Dictionary<string, Brush> _maskCache =
            new System.Collections.Generic.Dictionary<string, Brush>();

        private Brush EnsurePatternBrush(int cells, bool checker)
        {
            string key = (checker ? "chk" : "mos") + cells;
            Brush cached;
            if (_maskCache.TryGetValue(key, out cached)) return cached;

            const int px = 256;
            var wb = new WriteableBitmap(px, px, 96, 96, PixelFormats.Bgra32, null);
            int cell = Math.Max(1, px / cells);
            byte[] buf = new byte[px * px * 4];
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    bool on = checker
                        ? ((y / cell) + (x / cell)) % 2 == 0
                        : (x % cell > 2 && y % cell > 2);   // mosaic: плитки с зазором
                    int o = (y * px + x) * 4;
                    byte v = (byte)(on ? 255 : 0);
                    buf[o] = v; buf[o + 1] = v; buf[o + 2] = v; buf[o + 3] = v;
                }
            wb.WritePixels(new Windows.UI.Xaml.Int32Rect(0, 0, px, px), buf, px * 4, 0);

            var brush = new ImageBrush
            {
                ImageSource = wb,
                Stretch     = Stretch.Fill,
            };
            _maskCache[key] = brush;
            return brush;
        }

        // ─── Вспомогательные ──────────────────────────────────────────────────────

        private static DoubleAnimation Anim(double from, double to, TimeSpan dur,
            IEasingFunction ease = null)
            => new DoubleAnimation
               {
                   From = from, To = to,
                   Duration = new Duration(dur),
                   EasingFunction = ease,
                   FillBehavior = FillBehavior.Stop
               };

        private static void AttachDone(DoubleAnimation anim, Action cb)
            => anim.Completed += (_, __) => cb();

        private Size BoxSize
        {
            get
            {
                double w = _container.ActualWidth  > 1 ? _container.ActualWidth  : 1920;
                double h = _container.ActualHeight > 1 ? _container.ActualHeight : 1080;
                return new Size(w, h);
            }
        }

        private void ResetAll()
        {
            ResetTrans(_transA); ResetTrans(_transB);
        }

        private static void ResetTrans(CompositeTransform t)
        {
            t.ScaleX = t.ScaleY = 1;
            t.TranslateX = t.TranslateY = 0;
            t.Rotation = 0;
            t.SkewX = t.SkewY = 0;
        }

        private void ResetBackTransforms() => ResetTrans(BackTrans);

        private void CleanupEffects()
        {
            _imgA.OpacityMask = null; _imgB.OpacityMask = null;
        }

        private enum Dir { Left, Right, Up, Down }
    }
}
