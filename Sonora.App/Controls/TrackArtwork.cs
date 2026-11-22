using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Sonora.Controls;

public sealed class TrackArtwork : UserControl
{
    public static readonly StyledProperty<string?> ArtworkPathProperty =
        AvaloniaProperty.Register<TrackArtwork, string?>(nameof(ArtworkPath));

    private readonly Image image;
    private readonly TextBlock placeholder;
    private Bitmap? bitmap;

    public string? ArtworkPath
    {
        get => GetValue(ArtworkPathProperty);
        set => SetValue(ArtworkPathProperty, value);
    }

    static TrackArtwork()
    {
        ArtworkPathProperty.Changed.AddClassHandler<TrackArtwork>((control, _) => control.LoadArtwork());
    }

    public TrackArtwork()
    {
        ClipToBounds = true;
        var grid = new Grid();
        grid.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#2459A6"), 0),
                new GradientStop(Color.Parse("#176F9D"), 1)
            }
        };

        image = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
        placeholder = new TextBlock
        {
            Text = "♫",
            FontSize = 19,
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        grid.Children.Add(image);
        grid.Children.Add(placeholder);
        Content = grid;
        AttachedToVisualTree += (_, _) => LoadArtwork();
        DetachedFromVisualTree += (_, _) => ReleaseBitmap();
    }

    private void LoadArtwork()
    {
        ReleaseBitmap();
        if (string.IsNullOrWhiteSpace(ArtworkPath) || !File.Exists(ArtworkPath))
        {
            image.Source = null;
            image.IsVisible = false;
            placeholder.IsVisible = true;
            return;
        }

        try
        {
            bitmap = new Bitmap(ArtworkPath);
            image.Source = bitmap;
            image.IsVisible = true;
            placeholder.IsVisible = false;
        }
        catch
        {
            image.Source = null;
            image.IsVisible = false;
            placeholder.IsVisible = true;
        }
    }

    private void ReleaseBitmap()
    {
        image.Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }
}