using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Shared;
namespace ReFix.Hub
{
    internal sealed class SettingsWindow:Window
    {
        readonly ThemeStore themes;readonly Border frame,image;readonly Grid layers;bool closed;
        internal string Selection{get;private set;}
        internal SettingsWindow(ThemeStore themes,bool appearance=false)
        {
            this.themes=themes;Title="HUB ReFIX — настройки";
            Width=Math.Min(640,SystemParameters.WorkArea.Width);Height=Math.Min(appearance?420:590,SystemParameters.WorkArea.Height);MinWidth=420;
            ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            Ui.Styles(this);frame=new Border{Margin=new Thickness(16),BorderThickness=new Thickness(1)};Content=frame;
            layers=new Grid();frame.Child=layers;image=new Border{IsHitTestVisible=false};layers.Children.Add(image);
            Grid shell=new Grid{Margin=new Thickness(24)};shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layers.Children.Add(shell);
            Grid title=new Grid{Margin=new Thickness(0,0,0,22),Background=Brushes.Transparent};title.ColumnDefinitions.Add(new ColumnDefinition());title.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            StackPanel heading=new StackPanel();heading.Children.Add(Ui.Text(appearance?"Оформление":"Настройки",27,"Hub.Foreground"));var subtitle=Ui.Text(appearance?"Выберите, что настроить":"Управление HUB и расширениями",12,"Hub.Muted");subtitle.Margin=new Thickness(0,6,0,0);heading.Children.Add(subtitle);title.Children.Add(heading);
            Button close=Ui.Button("✕",Close,false);close.Width=34;close.Height=34;close.MinHeight=0;close.Padding=new Thickness(0);close.VerticalAlignment=VerticalAlignment.Top;close.ToolTip="Закрыть настройки";System.Windows.Automation.AutomationProperties.SetName(close,"Закрыть настройки");Grid.SetColumn(close,1);title.Children.Add(close);shell.Children.Add(title);
            title.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left)try{DragMove();}catch(InvalidOperationException){}};
            StackPanel sections=new StackPanel();
            if(appearance)
            {
                Add(sections,"\uE790","Общее оформление","Тема, цвета и стиль HUB и расширений","styles");
                Add(sections,"\uE767","Вид микшера","Размер окна, текста и ползунков","mixer-layout");
            }
            else
            {
                Add(sections,"\uE765","Управление","Назначение клавиш HUB и утилит","controls");
                Add(sections,"\uE790","Оформление","Общий стиль и параметры микшера","appearance");
                Add(sections,"\uE713","Поведение","Автозапуск и автоматическое скрытие","behavior");
                Add(sections,"\uE774","Ссылки","Папки и источники расширений","links");
            }
            var scroll=new ScrollViewer{Content=sections,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);shell.Children.Add(scroll);
            var footer=Ui.Text("HUB ReFIX · v"+ProductInfo.Version,11,"Hub.Muted");footer.Margin=new Thickness(0,16,0,0);Grid.SetRow(footer,2);shell.Children.Add(footer);
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Close();e.Handled=true;}};
            themes.Changed+=Changed;Closed+=delegate{closed=true;themes.Changed-=Changed;};SizeChanged+=delegate{Apply();};Apply();
        }
        void Add(Panel panel,string glyph,string title,string subtitle,string selection)
        {
            Button button=Ui.Button("",delegate{Selection=selection;DialogResult=true;},false);
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;button.Padding=new Thickness(16,14,16,14);button.MinHeight=82;button.Margin=new Thickness(0,0,0,10);
            System.Windows.Automation.AutomationProperties.SetName(button,title);System.Windows.Automation.AutomationProperties.SetHelpText(button,subtitle);
            Grid row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});
            var icon=Ui.Text(glyph,23,"Hub.Accent");icon.FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");icon.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(icon);
            StackPanel label=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var name=Ui.Text(title,16,"Hub.Foreground");name.FontWeight=FontWeights.SemiBold;label.Children.Add(name);var hint=Ui.Text(subtitle,12,"Hub.Muted");hint.Margin=new Thickness(0,5,0,0);label.Children.Add(hint);Grid.SetColumn(label,1);row.Children.Add(label);
            var arrow=Ui.Text("›",25,"Hub.Muted");arrow.VerticalAlignment=VerticalAlignment.Center;arrow.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(arrow,2);row.Children.Add(arrow);button.Content=row;panel.Children.Add(button);
        }
        void Apply(){Ui.Apply(this,themes.Current,frame,image,true);if(layers.ActualWidth>0&&layers.ActualHeight>0)layers.Clip=new RectangleGeometry(new Rect(0,0,layers.ActualWidth,layers.ActualHeight),frame.CornerRadius.TopLeft,frame.CornerRadius.TopLeft);}
        void Changed(object sender,EventArgs e){if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!closed)Apply();}));}
    }
}
