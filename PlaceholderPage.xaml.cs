using System.Windows.Controls;

namespace DayZServerManager.Views;

public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage(string title, string subtitle)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubText.Text = subtitle;
    }
}
