using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DisplayMGR.Views;

/// <summary>Lets the mouse wheel move a slider by <c>Step</c> per notch.</summary>
public static class SliderWheel
{
    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step", typeof(double), typeof(SliderWheel), new PropertyMetadata(0d, OnStepChanged));

    public static double GetStep(DependencyObject element) => (double)element.GetValue(StepProperty);

    public static void SetStep(DependencyObject element, double value) => element.SetValue(StepProperty, value);

    private static void OnStepChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Slider slider)
            return;
        slider.PreviewMouseWheel -= OnWheel;
        if ((double)e.NewValue > 0)
            slider.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var slider = (Slider)sender;
        double step = GetStep(slider) * slider.Maximum / 100; // Step is a percentage of the range
        slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * Math.Max(step, 1), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }
}
