using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DualShift.Localization;
using DualShift.Updates;

namespace DualShift.UI;

/// <summary>Okno nápovědy – obsah z <see cref="HelpContent"/>, přepíná se s jazykem aplikace.</summary>
public partial class HelpWindow : Window
{
    private bool _allowClose;
    private readonly UpdateService _updates;
    private readonly Border _updatesCard;

    /// <summary>Ruční kontrola aktualizací.</summary>
    public event EventHandler? CheckRequested;
    /// <summary>Nainstalovat / aktualizovat součást.</summary>
    public event EventHandler<UpdateComponent>? InstallRequested;
    /// <summary>Odstranit zastaralou aktualizační úlohu ViGEm.</summary>
    public event EventHandler? FixLegacyRequested;

    public HelpWindow(UpdateService updates)
    {
        _updates = updates;
        InitializeComponent();
        _updatesCard = new Border { Style = (Style)FindResource("Card") };
        _updates.Changed += () => Dispatcher.Invoke(BuildUpdatesCard);
        var work = SystemParameters.WorkArea;
        Height = Math.Min(Height, work.Height - 24);
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        try
        {
            using var ico = typeof(HelpWindow).Assembly.GetManifestResourceStream("Assets.app.ico");
            if (ico is not null) Icon = BitmapFrame.Create(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch
        {
            // bez ikony
        }

        Build();
        Loc.LanguageChanged += Build;
    }

    private void Build()
    {
        Toc.Children.Clear();
        Sections.Children.Clear();

        // Aktualizace a ovladače jako první karta.
        BuildUpdatesCard();
        Sections.Children.Add(_updatesCard);
        var updLink = new Button { Content = Loc.T("upd_title"), Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) };
        updLink.Click += (_, _) => ScrollToUpdates();
        Toc.Children.Add(updLink);

        foreach (var section in HelpContent.For(Loc.Language))
        {
            var card = new Border { Style = (Style)FindResource("Card") };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = section.Title, Style = (Style)FindResource("H2") });
            foreach (var block in section.Blocks)
                panel.Children.Add(Render(block));
            card.Child = panel;
            Sections.Children.Add(card);

            // Odkaz v obsahu – posune na sekci.
            var link = new Button { Content = section.Title, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) };
            link.Click += (_, _) => card.BringIntoView(new Rect(0, 0, 1, card.ActualHeight));
            Toc.Children.Add(link);
        }
        Scroller.ScrollToTop();
    }

    private UIElement Render(HelpContent.Block block) => block switch
    {
        HelpContent.Text t => new TextBlock
        {
            Text = t.Value,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 2, 0, 8),
            Foreground = (Brush)FindResource("Text"),
        },
        HelpContent.Bullets b => Bullets(b.Items),
        HelpContent.MappingTable m => Table(m),
        _ => new TextBlock(),
    };

    private UIElement Bullets(IEnumerable<string> items)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var item in items)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6, Height = 6, Fill = (Brush)FindResource("Accent"),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0),
            });
            var text = new TextBlock
            {
                Text = item, TextWrapping = TextWrapping.Wrap, LineHeight = 20,
                Foreground = (Brush)FindResource("Text"),
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        return panel;
    }

    private UIElement Table(HelpContent.MappingTable m)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddRow(int r, string a, string b, bool header)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bg = new Border
            {
                Background = header ? (Brush)FindResource("ControlBg") : r % 2 == 0 ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x35)) : Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
            };
            Grid.SetRow(bg, r);
            Grid.SetColumnSpan(bg, 2);
            grid.Children.Add(bg);
            foreach (var (text, col) in new[] { (a, 0), (b, 1) })
            {
                var t = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(12, 7, 12, 7),
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = (Brush)FindResource(header ? "Text" : col == 0 ? "Text" : "TextDim"),
                };
                Grid.SetRow(t, r);
                Grid.SetColumn(t, col);
                grid.Children.Add(t);
            }
        }

        AddRow(0, m.LeftHeader, m.RightHeader, true);
        for (int i = 0; i < m.Rows.Length; i++)
            AddRow(i + 1, m.Rows[i].Ds, m.Rows[i].Xbox, false);
        return grid;
    }

    public void ShowAndActivate(bool scrollToUpdates = false)
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (scrollToUpdates) Dispatcher.BeginInvoke(ScrollToUpdates, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollToUpdates() => _updatesCard.BringIntoView(new Rect(0, 0, 1, _updatesCard.ActualHeight));

    // ------------------------------------------------------------------ aktualizace

    private void BuildUpdatesCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Loc.T("upd_title"), Style = (Style)FindResource("H2"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock { Text = Loc.T("upd_subtitle"), Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 0, 0, 12) });

        bool locked = _updates.IsBusy || _updates.IsChecking;
        foreach (var s in _updates.All)
            panel.Children.Add(ComponentRow(s, locked));

        if (_updates.LegacyTasks.Count > 0)
            panel.Children.Add(LegacyRow(locked));

        // Ruční kontrola + stav poslední kontroly.
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var check = new Button
        {
            Content = Loc.T(_updates.IsChecking ? "upd_checking" : "upd_check"),
            IsEnabled = !locked,
        };
        check.Click += (_, _) => CheckRequested?.Invoke(this, EventArgs.Empty);
        footer.Children.Add(check);
        string info = _updates.LastError is { } err ? Loc.F("upd_checkFailed", err)
            : _updates.LastCheck is { } t ? Loc.F("upd_lastCheck", t)
            : Loc.T("upd_notChecked");
        footer.Children.Add(new TextBlock
        {
            Text = info, Style = (Style)FindResource("Dim"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        });
        panel.Children.Add(footer);

        _updatesCard.Child = panel;
    }

    private UIElement ComponentRow(ComponentStatus s, bool locked)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        string desc = s.Kind switch
        {
            UpdateComponent.App => Loc.T("upd_app_desc"),
            UpdateComponent.ViGEmBus => Loc.T("upd_vigem_desc"),
            _ => Loc.T("upd_hidhide_desc"),
        };
        text.Children.Add(new TextBlock { Text = $"{UpdateService.Name(s.Kind)} – {desc}", FontWeight = FontWeights.SemiBold });

        string installed = !s.Installed ? Loc.T("upd_notInstalled")
            : Loc.F("upd_installed", s.InstalledVersion is { } v ? UpdateService.Short(v) : Loc.T("upd_unknownVersion"));
        string latest = s.Latest is { } r ? " · " + Loc.F("upd_latest", UpdateService.Short(r.Version)) : "";
        text.Children.Add(new TextBlock
        {
            Text = installed + latest,
            Foreground = (Brush)FindResource(s.UpdateAvailable || !s.Installed ? "Accent" : "TextDim"),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
        });
        row.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!s.Installed || s.UpdateAvailable)
        {
            var action = new Button
            {
                Content = Loc.T(s.Installed ? "upd_update" : "upd_install"),
                Background = (Brush)FindResource("Accent"),
                IsEnabled = !locked && (s.Kind != UpdateComponent.App || s.Latest is not null),
                Margin = new Thickness(8, 0, 0, 0),
            };
            action.Click += (_, _) => InstallRequested?.Invoke(this, s.Kind);
            buttons.Children.Add(action);
        }
        else if (s.Latest is not null)
        {
            buttons.Children.Add(new TextBlock
            {
                Text = "✓ " + Loc.T("upd_upToDate"), Foreground = (Brush)FindResource("TextDim"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            });
        }
        string page = s.Latest?.PageUrl ?? (UpdateService.AsDependency(s.Kind) is { } d
            ? Setup.DependencyInstaller.ProjectPage(d)
            : $"https://github.com/{UpdateService.AppRepo}/releases/latest");
        var link = new Button { Content = Loc.T("upd_page"), Margin = new Thickness(8, 0, 0, 0) };
        link.Click += (_, _) => GitHub.OpenPage(page);
        buttons.Children.Add(link);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        return row;
    }

    private UIElement LegacyRow(bool locked)
    {
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0xA6, 0x23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 4),
        };
        var row = new DockPanel();
        var fix = new Button { Content = Loc.T("upd_fix"), IsEnabled = !locked, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        fix.Click += (_, _) => FixLegacyRequested?.Invoke(this, EventArgs.Empty);
        DockPanel.SetDock(fix, Dock.Right);
        row.Children.Add(fix);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "⚠ " + Loc.T("upd_legacyTitle"), FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = Loc.T("upd_legacyText"), Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(text);
        box.Child = row;
        return box;
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}

/// <summary>Tmavý titulek okna (Windows 10 20H1+ / 11).</summary>
internal static class DarkTitleBar
{
    public static void Apply(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            int caption = 0x00221D1B; // COLORREF (BGR) = #1B1D22
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        }
        catch
        {
            // Starší Windows – výchozí titulek.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
