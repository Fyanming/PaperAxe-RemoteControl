using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace ZhifaRemote.Controls;

public class RippleButton : System.Windows.Controls.Button
{
    static RippleButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RippleButton),
            new FrameworkPropertyMetadata(typeof(RippleButton)));
    }

    protected override void OnClick()
    {
        base.OnClick();
        PlayPress();
        PlayRipple();
    }

    private void PlayPress()
    {
        if (Template?.FindName("PART_Root", this) is not FrameworkElement root) return;
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = new TranslateTransform(0, 1);
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => root.RenderTransform = null;
        storyboard.Begin(root);
    }

    private void PlayRipple()
    {
        if (Template is null) return;
        if (Template.FindName("PART_RippleHost", this) is not Canvas host) return;
        if (Template.FindName("PART_Ripple", this) is not Ellipse ripple) return;

        var position = Mouse.GetPosition(this);
        var size = Math.Max(ActualWidth, ActualHeight) * 1.5;
        ripple.Width = size;
        ripple.Height = size;
        Canvas.SetLeft(ripple, position.X - size / 2);
        Canvas.SetTop(ripple, position.Y - size / 2);
        ripple.RenderTransformOrigin = new Point(0.5, 0.5);
        ripple.RenderTransform = new ScaleTransform(0.15, 0.15);
        ripple.Opacity = 1;

        var storyboard = new Storyboard();
        var scaleX = new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) =>
        {
            ripple.RenderTransform = null;
            ripple.Opacity = 0;
        };
        storyboard.Begin(ripple);
    }
}
