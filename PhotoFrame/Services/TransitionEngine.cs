// Services/TransitionEngine.cs
// Движок анимаций. Управляет двумя слоями Image (A и B).
// После перехода слои меняются ролями (front/back).
// Флип и Листание — однослойные (меняют источник «на лету»).

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public class TransitionEngine
    {
        // ─── Поля ─────────────────────────────────────────────────────────────────
        private readonly Image           _imgA, _imgB;
        private readonly FrameworkElement _container;

        private bool _isFrontA      = true;
        private bool _isTransitioning = false;

        private readonly ScaleTransform     _scaleA, _scaleB;
        private readonly TranslateTransform _transA, _transB;

        private static readonly Random _rng = new();

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

        // ─── Свойства front/back ──────────────────────────────────────────────────
        private Image           Front      => _isFrontA ? _imgA : _imgB;
        private Image           Back       => _isFrontA ? _imgB : _imgA;
        private ScaleTransform  FrontScale => _isFrontA ? _scaleA : _scaleB;
        private ScaleTransform  BackScale  => _isFrontA ? _scaleB : _scaleA;
        private TranslateTransform FrontTrans => _isFrontA ? _transA : _transB;
        private TranslateTransform BackTrans  => _isFrontA ? _transB : _transA;

        public bool IsTransitioning => _isTransitioning;

        // ─── Конструктор ──────────────────────────────────────────────────────────
        public TransitionEngine(Image imgA, Image imgB, FrameworkElement container)
        {
            _imgA = imgA; _imgB = imgB; _container = container;

            _scaleA = new ScaleTransform(1, 1);
            _transA = new TranslateTransform();
            _imgA.RenderTransformOrigin = new Point(0.5, 0.5);
            _imgA.RenderTransform = new TransformGroup { Children = { _scaleA, _transA } };

            _scaleB = new ScaleTransform(1, 1);
            _transB = new TranslateTransform();
            _imgB.RenderTransformOrigin = new Point(0.5, 0.5);
            _imgB.RenderTransform = new TransformGroup { Children = { _scaleB, _transB } };
        }

        // ─── Публичный API ────────────────────────────────────────────────────────

        public void ShowImmediate(BitmapSource? source)
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
            BitmapSource   newSource,
            TransitionType type,
            double         durationSec,
            Action?        onCompleted = null,
            bool           allowAdvanced = false)
        {
            // Free-сборка: allowAdvanced принимается для совместимости вызовов b62; Pro-гейт живёт в internal.
            _ = allowAdvanced;
            if (_isTransitioning) return;
            _isTransitioning = true;

            var actual = (type == TransitionType.Random)
                ? _concretes[_rng.Next(_concretes.Length)]
                : type;

            var dur = TimeSpan.FromSeconds(Math.Max(0.15, durationSec));

            Back.Source  = newSource;
            Back.Opacity = 0;
            Back.Effect  = null;
            Back.Clip    = null;
            ResetBackTransforms();

            // Standard swap-based complete (Fade, Slide, Zoom, Blur, Wipe, Checker, Mosaic, Spiral)
            void CompleteSwap()
            {
                Front.BeginAnimation(UIElement.OpacityProperty, null);
                Back.BeginAnimation(UIElement.OpacityProperty,  null);
                // v3.3: снимаем зависшие анимации трансформов (иначе FillBehavior.Stop
                // вернёт «до-анимационное» значение поверх нового кадра — визуальный баг)
                StopTransformAnimations(_scaleA, _transA);
                StopTransformAnimations(_scaleB, _transB);
                CleanupEffects();
                Back.Opacity  = 1;
                Back.Clip     = null;
                Front.Opacity = 0;
                ResetAll();
                _isFrontA        = !_isFrontA;
                _isTransitioning = false;
                onCompleted?.Invoke();
            }

            // No-swap complete (Flip, PageTurn, KenBurns — manage layers themselves)
            void CompleteNoSwap()
            {
                CleanupEffects();
                ResetAll();
                _isTransitioning = false;
                onCompleted?.Invoke();
            }

            switch (actual)
            {
                case TransitionType.Fade:         DoFade(dur, CompleteSwap);                    break;
                case TransitionType.SlideLeft:    DoSlide(dur, CompleteSwap, -1,  0);           break;
                case TransitionType.SlideRight:   DoSlide(dur, CompleteSwap,  1,  0);           break;
                case TransitionType.SlideUp:      DoSlide(dur, CompleteSwap,  0, -1);           break;
                case TransitionType.SlideDown:    DoSlide(dur, CompleteSwap,  0,  1);           break;
                case TransitionType.ZoomIn:       DoZoom(dur, CompleteSwap, true);              break;
                case TransitionType.ZoomOut:      DoZoom(dur, CompleteSwap, false);             break;
                case TransitionType.FlipH:        DoFlip(dur, CompleteNoSwap, true);            break;
                case TransitionType.FlipV:        DoFlip(dur, CompleteNoSwap, false);           break;
                case TransitionType.BlurDissolve: DoBlurDissolve(dur, CompleteSwap);            break;
                case TransitionType.WipeLeft:     DoWipe(dur, CompleteSwap, Dir.Left);          break;
                case TransitionType.WipeRight:    DoWipe(dur, CompleteSwap, Dir.Right);         break;
                case TransitionType.WipeUp:       DoWipe(dur, CompleteSwap, Dir.Up);            break;
                case TransitionType.WipeDown:     DoWipe(dur, CompleteSwap, Dir.Down);          break;
                case TransitionType.Checkerboard: DoCheckerboard(dur, CompleteSwap);            break;
                case TransitionType.KenBurns:     DoKenBurns(dur, CompleteNoSwap);              break;
                case TransitionType.Mosaic:       DoMosaic(dur, CompleteSwap);                  break;
                case TransitionType.Spiral:       DoSpiral(dur, CompleteSwap);                  break;
                case TransitionType.PageTurn:     DoPageTurn(dur, CompleteNoSwap);              break;
                default:                          DoFade(dur, CompleteSwap);                    break;
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
            BackTrans.X = -ox; BackTrans.Y = -oy;
            Back.Opacity = 1;
            var inX = Anim(-ox, 0, dur, ease); AttachDone(inX, done);
            BackTrans.BeginAnimation(TranslateTransform.XProperty, inX);
            BackTrans.BeginAnimation(TranslateTransform.YProperty, Anim(-oy, 0, dur, ease));
            FrontTrans.BeginAnimation(TranslateTransform.XProperty, Anim(0, ox, dur, ease));
            FrontTrans.BeginAnimation(TranslateTransform.YProperty, Anim(0, oy, dur, ease));
        }

        // ─── ZOOM ─────────────────────────────────────────────────────────────────
        private void DoZoom(TimeSpan dur, Action done, bool zoomIn)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            if (zoomIn)
            {
                BackScale.ScaleX = BackScale.ScaleY = 0.3; Back.Opacity = 0;
                var fi = Anim(0, 1, dur, ease); AttachDone(fi, done);
                BackScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.3, 1, dur, ease));
                BackScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.3, 1, dur, ease));
                Back.BeginAnimation(UIElement.OpacityProperty,  fi);
                Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, dur, ease));
            }
            else
            {
                Back.Opacity = 1;
                var sx = Anim(1, 0.3, dur, ease); AttachDone(sx, done);
                FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 0.3, dur, ease));
                Front.BeginAnimation(UIElement.OpacityProperty, Anim(1, 0, dur, ease));
            }
        }

        // ─── FLIP (однослойный) ───────────────────────────────────────────────────
        private void DoFlip(TimeSpan dur, Action done, bool horizontal)
        {
            var half = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            var eIn  = new SineEase { EasingMode = EasingMode.EaseIn };
            var col  = Anim(1, 0, half, eIn);
            col.Completed += (_, __) =>
            {
                Front.Source = Back.Source; Back.Source = null; Back.Opacity = 0;
                if (horizontal) FrontScale.ScaleY = 1.0; else FrontScale.ScaleX = 1.0;
                var exp = Anim(0, 1, half, new SineEase { EasingMode = EasingMode.EaseOut });
                exp.Completed += (_, __2) => { FrontScale.ScaleX = FrontScale.ScaleY = 1; done(); };
                if (horizontal) FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, exp);
                else            FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, exp);
            };
            if (horizontal) FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, col);
            else            FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, col);
        }

        // ─── BLUR DISSOLVE ────────────────────────────────────────────────────────
        private void DoBlurDissolve(TimeSpan dur, Action done)
        {
            var half = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            var blur = new BlurEffect { Radius = 0 };
            Front.Effect = blur;
            Back.Opacity  = 0;
            var ba = Anim(0, 28, half, new CubicEase { EasingMode = EasingMode.EaseIn });
            var fo = Anim(1, 0,  half, new CubicEase { EasingMode = EasingMode.EaseIn });
            ba.Completed += (_, __) =>
            {
                Front.Effect = null; Front.Opacity = 0;
                var fi = Anim(0, 1, half, new CubicEase { EasingMode = EasingMode.EaseOut });
                fi.Completed += (_, __2) => done();
                Back.BeginAnimation(UIElement.OpacityProperty, fi);
            };
            blur.BeginAnimation(BlurEffect.RadiusProperty,   ba);
            Front.BeginAnimation(UIElement.OpacityProperty,  fo);
        }

        // ─── WIPE ─────────────────────────────────────────────────────────────────
        private void DoWipe(TimeSpan dur, Action done, Dir dir)
        {
            var size = BoxSize;
            var ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
            double w = size.Width, h = size.Height;
            Rect start, end;
            switch (dir)
            {
                case Dir.Left:  start = new Rect(w, 0, 0, h); end = new Rect(0, 0, w, h); break;
                case Dir.Right: start = new Rect(0, 0, 0, h); end = new Rect(0, 0, w, h); break;
                case Dir.Up:    start = new Rect(0, h, w, 0); end = new Rect(0, 0, w, h); break;
                default:        start = new Rect(0, 0, w, 0); end = new Rect(0, 0, w, h); break;
            }
            var clip = new RectangleGeometry(start);
            Back.Clip = clip; Back.Opacity = 1;
            var ra = new RectAnimation(start, end, new Duration(dur)) { EasingFunction = ease };
            ra.Completed += (_, __) => { Back.Clip = null; done(); };
            clip.BeginAnimation(RectangleGeometry.RectProperty, ra);
        }

        // ─── CHECKERBOARD ─────────────────────────────────────────────────────────
        // Имитируется через постепенное увеличение opacity с OpacityMask в шахматный паттерн.
        // Так как WPF не поддерживает анимируемый DrawingBrush-паттерн напрямую,
        // реализуем через двухшаговый fade с немного сдвинутым ZoomIn — создаёт иллюзию шахматки.
        private void DoCheckerboard(TimeSpan dur, Action done)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            // Делаем 8 ступеней opacity с VisualBrush как OpacityMask
            var tileW = Math.Max(BoxSize.Width / 8,  1);
            var tileH = Math.Max(BoxSize.Height / 8, 1);

            // Checkerboard DrawingBrush
            var drawing = new DrawingGroup();
            bool filled = false;
            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    if ((row + col) % 2 == 0)
                    {
                        drawing.Children.Add(new GeometryDrawing(
                            Brushes.White, null,
                            new RectangleGeometry(new Rect(col * tileW, row * tileH, tileW, tileH))));
                        filled = true;
                    }
                }
            }
            _ = filled; // suppress warning

            var mask = new DrawingBrush
            {
                Drawing  = drawing,
                Stretch  = Stretch.Fill,
                Viewport = new Rect(0, 0, BoxSize.Width, BoxSize.Height),
                ViewportUnits = BrushMappingMode.Absolute,
            };

            Back.OpacityMask = mask;
            Back.Opacity     = 1;

            // Animate Front fade out — the checkerboard mask on Back "reveals" in
            var fadeOut = Anim(1, 0, dur, ease);
            AttachDone(fadeOut, () => { Back.OpacityMask = null; done(); });
            Front.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // ─── KEN BURNS (zoom + pan, однослойный) ──────────────────────────────────
        private void DoKenBurns(TimeSpan dur, Action done)
        {
            // Показываем новое фото сразу, затем анимируем zoom+pan
            Front.Source  = Back.Source;
            Back.Source   = null;
            Back.Opacity  = 0;
            Front.Opacity = 1;

            // Начинаем с маленького масштаба + смещения → к 1.0 + центр
            double startScale = 1.15;
            double offX = (_rng.NextDouble() - 0.5) * BoxSize.Width  * 0.1;
            double offY = (_rng.NextDouble() - 0.5) * BoxSize.Height * 0.1;

            FrontScale.ScaleX = FrontScale.ScaleY = startScale;
            FrontTrans.X = offX; FrontTrans.Y = offY;

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var sx = Anim(startScale, 1, dur, ease); AttachDone(sx, done);
            FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(startScale, 1, dur, ease));
            FrontTrans.BeginAnimation(TranslateTransform.XProperty,  Anim(offX, 0, dur, ease));
            FrontTrans.BeginAnimation(TranslateTransform.YProperty,  Anim(offY, 0, dur, ease));
        }

        // ─── MOSAIC ───────────────────────────────────────────────────────────────
        // Аппроксимация: ZoomOut старого + ZoomIn нового с задержкой создаёт «мозаичный» эффект
        private void DoMosaic(TimeSpan dur, Action done)
        {
            var ease  = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var half  = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            Back.Opacity = 0;

            // Blur + scale front down
            var blur = new BlurEffect { Radius = 0 };
            Front.Effect = blur;
            blur.BeginAnimation(BlurEffect.RadiusProperty, Anim(0, 20, half, ease));
            FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 1.3, half, ease));
            FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 1.3, half, ease));

            var fo = Anim(1, 0, half, ease);
            fo.Completed += (_, __) =>
            {
                Front.Effect = null;
                Front.Opacity = 0;
                BackScale.ScaleX = BackScale.ScaleY = 1.3;

                var backBlur = new BlurEffect { Radius = 20 };
                Back.Effect  = backBlur;
                Back.Opacity = 1;
                backBlur.BeginAnimation(BlurEffect.RadiusProperty,   Anim(20, 0, half, ease));
                BackScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.3, 1, half, ease));
                BackScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.3, 1, half, ease));

                var fi = Anim(0, 1, half, ease); fi.Completed += (_, __2) => done();
                Back.BeginAnimation(UIElement.OpacityProperty, fi);
            };
            Front.BeginAnimation(UIElement.OpacityProperty, fo);
        }

        // ─── SPIRAL (rotate + fade) ───────────────────────────────────────────────
        private void DoSpiral(TimeSpan dur, Action done)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // v3.3: фиксируем конкретный TransformGroup ДО возможной смены слоёв —
            // иначе done() удаляет skew/rotate не из того слоя (утечка трансформов).
            var rotate = new RotateTransform(0);
            var frontTg = Front.RenderTransform as TransformGroup;
            if (frontTg != null)
                frontTg.Children.Add(rotate);

            Back.Opacity = 0;

            var rotAnim = new DoubleAnimation(0, 180, new Duration(dur))
                { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var scaleOut = Anim(1, 0, dur, ease);
            var fadeIn   = Anim(0, 1, dur, ease);
            AttachDone(fadeIn, () =>
            {
                // Убираем RotateTransform именно из того слоя, куда добавляли
                frontTg?.Children.Remove(rotate);
                done();
            });

            rotate.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
            FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
            FrontScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 0, dur, ease));
            Front.BeginAnimation(UIElement.OpacityProperty,          Anim(1, 0, dur, ease));
            Back.BeginAnimation(UIElement.OpacityProperty,           fadeIn);
        }

        // ─── PAGE TURN (однослойный — имитируется skew + scale) ─────────────────
        private void DoPageTurn(TimeSpan dur, Action done)
        {
            var half = TimeSpan.FromSeconds(dur.TotalSeconds / 2);
            var eIn  = new CubicEase { EasingMode = EasingMode.EaseIn };

            // SkewTransform даёт ощущение загибания страницы
            // v3.3: то же исправление, что и в Spiral — удаление строго из исходного tg.
            var skew = new SkewTransform(0, 0);
            var pageTg = Front.RenderTransform as TransformGroup;
            if (pageTg != null)
                pageTg.Children.Add(skew);

            var skewAnim = new DoubleAnimation(0, -20, new Duration(half))
                { EasingFunction = eIn, FillBehavior = FillBehavior.Stop };
            var narrowX  = Anim(1, 0, half, eIn);
            var fadeOut  = Anim(1, 0, half, eIn);

            narrowX.Completed += (_, __) =>
            {
                pageTg?.Children.Remove(skew);

                Front.Source = Back.Source; Back.Source = null;
                Front.Opacity = 1;
                FrontScale.ScaleX = 0;
                var expandX = Anim(0, 1, half, new CubicEase { EasingMode = EasingMode.EaseOut });
                expandX.Completed += (_, __2) => done();
                FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, expandX);
            };

            skew.BeginAnimation(SkewTransform.AngleXProperty, skewAnim);
            FrontScale.BeginAnimation(ScaleTransform.ScaleXProperty, narrowX);
            Front.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // ─── Вспомогательные ──────────────────────────────────────────────────────

        private static void StopTransformAnimations(ScaleTransform s, TranslateTransform t)
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private static DoubleAnimation Anim(double from, double to, TimeSpan dur,
            IEasingFunction? ease = null)
            => new DoubleAnimation(from, to, new Duration(dur))
               { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

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
            _scaleA.ScaleX = _scaleA.ScaleY = 1; _scaleB.ScaleX = _scaleB.ScaleY = 1;
            _transA.X = _transA.Y = 0;           _transB.X = _transB.Y = 0;
        }

        private void ResetBackTransforms()
        {
            BackScale.ScaleX = BackScale.ScaleY = 1;
            BackTrans.X = BackTrans.Y = 0;
        }

        private void CleanupEffects()
        {
            _imgA.Effect = null; _imgB.Effect = null;
            _imgA.Clip   = null; _imgB.Clip   = null;
            _imgA.OpacityMask = null; _imgB.OpacityMask = null;
        }

        private enum Dir { Left, Right, Up, Down }
    }
}
