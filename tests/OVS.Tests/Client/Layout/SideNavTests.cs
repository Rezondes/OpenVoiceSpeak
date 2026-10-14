using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Layout;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Layout;

/// <summary>Package 151: a tab control with the class sideNav lists its pages on the left, below 800 px list or page.</summary>
[Collection(MotionCollection.Name)]
public sealed class SideNavTests
{
    readonly Button inside = new() { Content = "Inside" };
    TabControl tabs = null!;
    Window window = null!;

    void Show(double width)
    {
        tabs = new TabControl
        {
            Classes = { "sideNav" },
            Items =
            {
                new TabItem { Classes = { "navGroup" }, Header = "GROUP" },
                new TabItem { Header = "First", Content = inside },
                new TabItem { Header = "Second", Content = new TextBlock { Text = "Two" } },
                new TabItem { Header = "Third", Content = new TextBlock { Text = "Three" } },
            },
            SelectedIndex = 1,
        };
        Responsive.SetStackBelow(tabs, 800);
        SideNav.SetListDetail(tabs, true);
        window = new Window { Width = width, Height = 500, Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    Control Part(string name) => tabs.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);

    TabItem Item(int index) => (TabItem)tabs.ContainerFromIndex(index)!;

    void Click(Control control)
    {
        var point = Rows.Center(control, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    void Press(Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Wide_ListAndPage_SideBySide()
    {
        Show(1000);
        try
        {
            var nav = Part("PART_Nav");
            var page = Part("PART_Page");
            Assert.True(nav.IsEffectivelyVisible && page.IsEffectivelyVisible);
            Assert.Equal(232, nav.Bounds.Width, 1);
            Assert.True(page.TranslatePoint(default, window)!.Value.X >= nav.Bounds.Width - 1);
            Assert.False(Part("PART_Back").IsEffectivelyVisible);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "First" && textBlock.Classes.Contains("h1"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void GroupTitle_ClickedOrArrowed_NeverChosen()
    {
        Show(1000);
        try
        {
            var group = Item(0);
            Assert.False(group.IsEnabled);
            Assert.False(group.Focusable);
            Click(group);
            Assert.Equal(1, tabs.SelectedIndex);
            Item(1).Focus();
            Press(Key.Up);
            Assert.Equal(1, tabs.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Narrow_ClickOpensPage_BackShowsList()
    {
        Show(600);
        try
        {
            Assert.True(Part("PART_Nav").IsEffectivelyVisible);
            Assert.False(Part("PART_Page").IsEffectivelyVisible);

            Click(Item(2));
            Assert.Equal(2, tabs.SelectedIndex);
            Assert.False(Part("PART_Nav").IsEffectivelyVisible);
            Assert.True(Part("PART_Page").IsEffectivelyVisible);
            Assert.Equal(window.Bounds.Width, Part("PART_Page").Bounds.Width, 1);

            Click(Part("PART_Back"));
            Assert.True(Part("PART_Nav").IsEffectivelyVisible);
            Assert.False(Part("PART_Page").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Narrow_ArrowKeys_StayOnList()
    {
        Show(600);
        try
        {
            Item(1).Focus();
            Press(Key.Down);
            Assert.Equal(2, tabs.SelectedIndex);
            Assert.True(Part("PART_Nav").IsEffectivelyVisible);

            Press(Key.Enter);
            Assert.True(Part("PART_Page").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DataContext_New_StartsOnList()
    {
        Show(600);
        try
        {
            SideNav.ShowPage(tabs);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Part("PART_Page").IsEffectivelyVisible);

            tabs.DataContext = new object();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Part("PART_Nav").IsEffectivelyVisible);
            Assert.False(Part("PART_Page").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PageClick_Inside_ListStaysHidden()
    {
        Show(600);
        try
        {
            Click(Item(1));
            Click(inside);
            Assert.True(Part("PART_Page").IsEffectivelyVisible);
            Assert.False(Part("PART_Nav").IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
