using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Sonora
{
    // Экран загрузки: логотип со свечением, название фирменным шрифтом и тонкая «бегущая» полоска
    public sealed class Splash : Grid
    {
        readonly TextBlock status;
        readonly ScaleTransform logoScale = new ScaleTransform(.86, .86);
        readonly bool still;
        bool done;

        static Ellipse Glow(Color c, double a, double w, double h, HorizontalAlignment ha, VerticalAlignment va, Thickness m)
        {
            var br = new RadialGradientBrush();
            br.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B), 0));
            br.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 90), c.R, c.G, c.B), .55));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            return new Ellipse { Width = w, Height = h, Fill = br, HorizontalAlignment = ha, VerticalAlignment = va, Margin = m, IsHitTestVisible = false };
        }

        public Splash(bool shotMode)
        {
            still = shotMode;
            Background = Th.Bg;
            ClipToBounds = true;
            System.Windows.Documents.TextElement.SetFontFamily(this, Th.Body);
            var g1 = Glow(Th.C("#2563EB"), .34, 900, 700, HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0, -60, 0, 0));
            var g2 = Glow(Th.C("#1E3A8A"), .45, 1200, 800, HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-500, 0, 0, -460));
            var g3 = Glow(Th.C("#60A5FA"), .14, 900, 600, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -380, -380, 0));
            Children.Add(g2); Children.Add(g3); Children.Add(g1);

            // логотип
            FrameworkElement logo;
            var src = Img.Res("logo.png");
            if (src != null)
            {
                var im = new Image { Source = src, Width = 116, Height = 116, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                logo = im;
            }
            else logo = U.Circle(116, Th.Accent, U.Center(U.Ico("wave", 54, Th.White)));
            logo.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Color = Th.C("#3B82F6"), Opacity = .75 };
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            logo.RenderTransformOrigin = new Point(.5, .5);
            logo.RenderTransform = logoScale;

            var name = new TextBlock { Text = "SONORA", FontFamily = Th.LogoFont, FontSize = 46, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 8) };
            name.Foreground = new LinearGradientBrush(Colors.White, Th.C("#93C5FD"), new Point(0, 0), new Point(1, 1));
            var tag = new TextBlock { Text = L.T("Вся твоя музыка — в одном месте"), FontSize = 14, Foreground = Th.Dim, HorizontalAlignment = HorizontalAlignment.Center };

            // тонкая полоска загрузки с бегущим бликом
            var runner = new Border { Width = 90, Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Th.C("#3B82F6", 0), 0), new GradientStop(Th.C("#93C5FD"), .5), new GradientStop(Th.C("#3B82F6", 0), 1) }, new Point(0, .5), new Point(1, .5)) };
            var runT = new TranslateTransform(-90, 0);
            runner.RenderTransform = runT;
            var track = new Grid { Width = 220, Height = 3, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 38, 0, 14) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Th.B("#FFFFFF", .08) });
            track.Children.Add(runner);
            status = new TextBlock { Text = L.T("Загружаем музыку…"), FontSize = 12.5, Foreground = Th.B("#FFFFFF", .5), HorizontalAlignment = HorizontalAlignment.Center };

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
            col.Children.Add(logo); col.Children.Add(name); col.Children.Add(tag); col.Children.Add(track); col.Children.Add(status);
            Children.Add(col);

            var ver = new TextBlock { Text = L.T("версия ") + Program.Version, FontSize = 11.5, Foreground = Th.B("#FFFFFF", .32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 26) };
            Children.Add(ver);

            if (still) { logoScale.ScaleX = logoScale.ScaleY = 1; runT.X = 65; return; }
            // анимации появления
            var ease = new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut };
            var sc = new DoubleAnimation(.86, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease };
            logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
            logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            Fade(logo, 0, 1, 500, 0);
            Fade(name, 0, 1, 600, 180);
            Fade(tag, 0, 1, 600, 320);
            Fade(track, 0, 1, 500, 420);
            Fade(status, 0, 1, 500, 480);
            runT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-90, 220, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            // «дыхание» свечения
            var glowA = new DoubleAnimation(.55, 1, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            g1.BeginAnimation(OpacityProperty, glowA);
            ((DropShadowEffect)logo.Effect).BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(.45, .85, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        static void Fade(UIElement el, double from, double to, int ms, int delay)
        {
            el.Opacity = from;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        public void SetStatus(string s) { status.Text = L.T(s); }

        public void Finish()
        {
            if (done) return;
            done = true;
            IsHitTestVisible = false;
            if (still) { (Parent as Panel)?.Children.Remove(this); return; }
            var st = new ScaleTransform(1, 1);
            RenderTransformOrigin = new Point(.5, .5);
            RenderTransform = st;
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (s, e) => (Parent as Panel)?.Children.Remove(this);
            BeginAnimation(OpacityProperty, a);
            var z = new DoubleAnimation(1, 1.04, TimeSpan.FromMilliseconds(480));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, z);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, z);
        }
    }
}

namespace Sonora
{
    // Тонкие серые полосы прокрутки без стрелок — минимализм
    public static class Styles
    {
        const string Thumb = @"<ControlTemplate TargetType='Thumb'><Border x:Name='b' CornerRadius='3' Background='#33FFFFFF'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#59FFFFFF'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='b' Property='Background' Value='#73FFFFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
        public static readonly string ScrollBars = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='SnThumb' TargetType='Thumb'><Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='IsTabStop' Value='False'/><Setter Property='Template'><Setter.Value>" + Thumb + @"</Setter.Value></Setter></Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,6,3,6'><Track.Thumb><Thumb Style='{StaticResource SnThumb}' MinHeight='28'/></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'>
      <Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='10'/><Setter Property='MinHeight' Value='10'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='False' Margin='6,3,6,3'><Track.Thumb><Thumb Style='{StaticResource SnThumb}' MinWidth='28'/></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter>
    </Trigger></Style.Triggers>
  </Style>
</ResourceDictionary>";
    }
}
