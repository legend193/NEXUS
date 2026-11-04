using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexus.App.ViewModels;

namespace Nexus.App.Views;

public partial class SonarPopoutWindow : Window
{
    private Point _dragStartPoint;

    public SonarPopoutWindow()
    {
        InitializeComponent();
    }

    public SonarPopoutWindow(AudioStudioViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AppIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void AppIcon_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point currentPoint = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is FrameworkElement element && element.DataContext is AudioSessionItem session)
            {
                DragDrop.DoDragDrop(element, session, DragDropEffects.Move);
            }
        }
    }

    private void ChannelTray_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(AudioSessionItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void ChannelTray_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement tray && tray.Tag is string targetChannel)
        {
            if (e.Data.GetData(typeof(AudioSessionItem)) is AudioSessionItem session)
            {
                if (DataContext is AudioStudioViewModel vm)
                {
                    vm.AssignSessionToChannel(session, targetChannel);
                }
            }
        }
    }

    private void AppIcon_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is AudioSessionItem session)
        {
            var cm = new ContextMenu();
            var m1 = new MenuItem { Header = "نقل إلى قناة الألعاب (Game)" };
            m1.Click += (_, _) => (DataContext as AudioStudioViewModel)?.AssignSessionToChannel(session, "Game Channel");
            cm.Items.Add(m1);

            var m2 = new MenuItem { Header = "نقل إلى قناة المحادثات (Chat)" };
            m2.Click += (_, _) => (DataContext as AudioStudioViewModel)?.AssignSessionToChannel(session, "Chat Channel");
            cm.Items.Add(m2);

            var m3 = new MenuItem { Header = "نقل إلى قناة الوسائط (Media)" };
            m3.Click += (_, _) => (DataContext as AudioStudioViewModel)?.AssignSessionToChannel(session, "Media Channel");
            cm.Items.Add(m3);

            var m4 = new MenuItem { Header = "نقل إلى القناة الإضافية (Aux)" };
            m4.Click += (_, _) => (DataContext as AudioStudioViewModel)?.AssignSessionToChannel(session, "Aux Channel");
            cm.Items.Add(m4);

            cm.Items.Add(new Separator());

            var mMute = new MenuItem { Header = session.IsMuted ? "إلغاء كتم صوت التطبيق" : "كتم صوت التطبيق" };
            mMute.Click += (_, _) => session.IsMuted = !session.IsMuted;
            cm.Items.Add(mMute);

            cm.PlacementTarget = el;
            cm.IsOpen = true;
            e.Handled = true;
        }
    }
}
