using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal sealed class ControlsWindow:Window
    {
        private readonly ThemeStore themes;
        private readonly ControlsStore controls;
        private readonly Border frame,image;
        private readonly TextBlock notice=Ui.Text("Выберите действие и нажмите новое сочетание.",12,"Hub.Muted");
        private readonly Dictionary<string,Button> assignments=new Dictionary<string,Button>();
        private readonly Dictionary<string,TextBox> typedAssignments=new Dictionary<string,TextBox>();
        private readonly Dictionary<string,TextBlock> states=new Dictionary<string,TextBlock>();
        private readonly Dictionary<string,Border> keys=new Dictionary<string,Border>();
        private readonly CheckBox mixerStartup=new CheckBox { Content="Запускать установленный микшер вместе с HUB" };
        private string selected,capturing;
        private bool updating;
        internal ControlsWindow(ThemeStore themeStore,ControlsStore controlStore)
        {
            themes=themeStore;controls=controlStore;Title="HUB ReFIX — управление";Width=620;Height=820;MinWidth=530;MinHeight=500;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.CanResizeWithGrip;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Ui.Styles(this);
            frame=new Border { Margin=new Thickness(18),BorderThickness=new Thickness(1) };frame.SetResourceReference(BackgroundProperty,"Hub.Background");frame.SetResourceReference(BorderBrushProperty,"Hub.Border");Content=frame;
            Grid layers=new Grid();frame.Child=layers;image=new Border { IsHitTestVisible=false };image.SetResourceReference(BackgroundProperty,"Hub.Image");layers.Children.Add(image);
            Grid layout=new Grid { Margin=new Thickness(20,16,20,16) };layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });layers.Children.Add(layout);
            DockPanel title=new DockPanel { Margin=new Thickness(0,0,0,14),Background=Brushes.Transparent };
            Button close=Ui.Button("✕",Close,false);close.Width=32;close.Padding=new Thickness(0);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Ui.Text("Управление",23,"Hub.Foreground"));
            title.MouseLeftButtonDown+=delegate{try{DragMove();}catch(InvalidOperationException){}};layout.Children.Add(title);
            StackPanel content=new StackPanel();content.Children.Add(Ui.Text("Назначение клавиш",18,"Hub.Foreground"));
            TextBlock explanation=Ui.Text("Глобальные сочетания работают без открытого окна. Можно записать нажатие или ввести сочетание текстом и нажать Enter. Текстовый ввод подходит и для уже занятых клавиш.",12,"Hub.Muted");explanation.Margin=new Thickness(0,8,0,16);content.Children.Add(explanation);
            string previous=null;
            foreach(ControlAction action in ControlActions.All)
            {
                string group=action.Scope=="global"?"Глобальные сочетания":"Клавиши в окнах";
                if(group!=previous){TextBlock heading=Ui.Text(group,14,"Hub.Foreground");heading.Margin=new Thickness(0,12,0,8);content.Children.Add(heading);previous=group;}
                content.Children.Add(ActionRow(action));
            }
            Border line=new Border { Height=1,Margin=new Thickness(0,18,0,12) };line.SetResourceReference(BackgroundProperty,"Hub.Border");content.Children.Add(line);
            content.Children.Add(Ui.Text("Раскладка клавиатуры",15,"Hub.Foreground"));
            TextBlock physical=Ui.Text("Латинские и русские обозначения одной физической клавиши. Подсвечено выбранное назначение.",11,"Hub.Muted");physical.Margin=new Thickness(0,6,0,10);content.Children.Add(physical);
            content.Children.Add(BuildKeyboard());
            TextBlock standard=Ui.Text("Во время ввода: Esc — отменить, Backspace / Delete — убрать назначение. В микшере стрелки меняют громкость, Tab переключает элементы — это стандартное управление ползунками.",11,"Hub.Muted");standard.Margin=new Thickness(0,12,0,12);content.Children.Add(standard);
            mixerStartup.Margin=new Thickness(0,12,0,10);mixerStartup.Click+=delegate
            {
                if(updating)return;try{ControlsProfile p=controls.Current;p.StartMixerWithHub=mixerStartup.IsChecked==true;controls.Save(p);notice.Text="Настройка запуска сохранена.";}catch(Exception ex){notice.Text=ex.Message;Refresh();}
            };content.Children.Add(mixerStartup);
            Button reset=Ui.Button("Восстановить стандартные клавиши",delegate
            {
                try{capturing=null;controls.ResetDefaults();Refresh();notice.Text="Стандартные назначения восстановлены.";}catch(Exception ex){notice.Text=ex.Message;}
            },false);reset.HorizontalAlignment=HorizontalAlignment.Left;reset.Margin=new Thickness(0,6,0,12);content.Children.Add(reset);
            ScrollViewer scroll=new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0) };Grid.SetRow(scroll,1);layout.Children.Add(scroll);
            notice.Margin=new Thickness(0,14,0,0);Grid.SetRow(notice,2);layout.Children.Add(notice);
            PreviewKeyDown+=Capture;
            themes.Changed+=ThemeChanged;controls.Changed+=BindingsChanged;controls.RuntimeChanged+=BindingsChanged;
            Closed+=delegate{themes.Changed-=ThemeChanged;controls.Changed-=BindingsChanged;controls.RuntimeChanged-=BindingsChanged;};
            SizeChanged+=delegate{layers.Clip=new RectangleGeometry(new Rect(0,0,layers.ActualWidth,layers.ActualHeight),frame.CornerRadius.TopLeft,frame.CornerRadius.TopLeft);ApplyTheme();};
            ApplyTheme();Refresh();
        }
        private UIElement ActionRow(ControlAction action)
        {
            Grid grid=new Grid { Margin=new Thickness(0,0,0,10) };grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(175) });grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(36) });
            StackPanel text=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0) };text.Children.Add(Ui.Text(action.Name,12,"Hub.Foreground"));
            TextBlock status=Ui.Text("",10,"Hub.Muted");status.Margin=new Thickness(0,3,0,0);text.Children.Add(status);states[action.Id]=status;grid.Children.Add(text);
            StackPanel entry=new StackPanel();Grid.SetColumn(entry,1);grid.Children.Add(entry);
            TextBox typed=new TextBox { Padding=new Thickness(8,5,8,5),ToolTip="Например Ctrl+Alt+H. Enter — применить; пустая строка — отключить." };typedAssignments[action.Id]=typed;entry.Children.Add(typed);
            System.Windows.Automation.AutomationProperties.SetName(typed,"Сочетание: "+action.Name);
            typed.GotKeyboardFocus+=delegate{selected=action.Id;Refresh();};
            typed.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){Assign(action.Id,typed.Text.Trim());e.Handled=true;}};
            Button assign=Ui.Button("",delegate{capturing=action.Id;selected=action.Id;Refresh();notice.Text="Нажмите новое сочетание для «"+action.Name+"». Занятые клавиши можно ввести текстом. Esc отменяет ввод.";Focus();},false);assign.ToolTip=action.Description;assign.Padding=new Thickness(6,3,6,3);assign.MinHeight=24;assign.FontSize=10;assign.Margin=new Thickness(0,4,0,0);assignments[action.Id]=assign;entry.Children.Add(assign);
            System.Windows.Automation.AutomationProperties.SetName(assign,"Назначение: "+action.Name);
            Button clear=Ui.Button("×",delegate{Assign(action.Id,"");},false);clear.ToolTip="Убрать назначение";clear.Margin=new Thickness(5,0,0,0);clear.Padding=new Thickness(0);Grid.SetColumn(clear,2);grid.Children.Add(clear);return grid;
        }
        private UIElement BuildKeyboard()
        {
            StackPanel keyboard=new StackPanel();
            string[][] rows={new[]{"Esc","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"},new[]{"1","2","3","4","5","6","7","8","9","0"},new[]{"Q/Й","W/Ц","E/У","R/К","T/Е","Y/Н","U/Г","I/Ш","O/Щ","P/З"},new[]{"A/Ф","S/Ы","D/В","F/А","G/П","H/Р","J/О","K/Л","L/Д"},new[]{"Z/Я","X/Ч","C/С","V/М","B/И","N/Т","M/Ь","Enter"},new[]{"Ctrl","Alt","Shift","Win","Space","Left","Down","Up","Right"}};
            foreach(string[] row in rows)
            {
                UniformGrid grid=new UniformGrid { Columns=row.Length,Margin=new Thickness(0,0,4,4) };
                foreach(string key in row)
                {
                    string token=key.Split('/')[0];TextBlock label=Ui.Text(key.Replace("/","\n"),10,"Hub.Foreground");label.TextAlignment=TextAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;
                    Border cap=new Border { Child=label,MinHeight=key.Contains("/")?34:25,CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,4,0),Padding=new Thickness(1) };cap.SetResourceReference(BackgroundProperty,"Hub.Surface");cap.SetResourceReference(BorderBrushProperty,"Hub.Border");keys[token]=cap;grid.Children.Add(cap);
                }
                keyboard.Children.Add(grid);
            }
            return keyboard;
        }
        private void Capture(object sender,KeyEventArgs e)
        {
            Key key=e.Key==Key.System?e.SystemKey:e.Key;
            if(capturing==null){if(key==Key.Escape)Close();return;}
            e.Handled=true;
            if(key==Key.Escape){capturing=null;Refresh();notice.Text="Изменение отменено.";return;}
            if(key==Key.LeftCtrl||key==Key.RightCtrl||key==Key.LeftAlt||key==Key.RightAlt||key==Key.LeftShift||key==Key.RightShift||key==Key.LWin||key==Key.RWin)return;
            if((key==Key.Back||key==Key.Delete)&&Keyboard.Modifiers==ModifierKeys.None){Assign(capturing,"");return;}
            ModifierKeys modifiers=Keyboard.Modifiers;
            string gesture=((modifiers&ModifierKeys.Control)!=0?"Ctrl+":"")+((modifiers&ModifierKeys.Alt)!=0?"Alt+":"")+((modifiers&ModifierKeys.Shift)!=0?"Shift+":"")+((modifiers&ModifierKeys.Windows)!=0?"Win+":"");
            gesture+=((System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(key)).ToString();Assign(capturing,gesture);
        }
        private void Assign(string id,string value)
        {
            try{controls.Assign(id,value);selected=id;capturing=null;notice.Text="Назначение сохранено и передано работающим программам.";}catch(Exception ex){notice.Text=ex.Message;capturing=null;}Refresh();
        }
        private void Refresh()
        {
            ControlsProfile p=controls.Current;
            foreach(ControlAction action in ControlActions.All)
            {
                string gesture=p.Get(action.Id);assignments[action.Id].Content=capturing==action.Id?"Нажмите клавиши…":"Записать нажатие";
                if(!typedAssignments[action.Id].IsKeyboardFocusWithin)typedAssignments[action.Id].Text=gesture??"";
                if(action.Scope!="global")states[action.Id].Text="В окне программы · по умолчанию "+(string.IsNullOrEmpty(action.DefaultGesture)?"нет":action.DefaultGesture);
                else
                {
                    HotkeyRuntimeStatus state=controls.GetRuntime(action.Id);
                    states[action.Id].Text=state==null?"Не активно: программа не запущена":!string.IsNullOrEmpty(state.Error)?"Конфликт. Действует: "+(state.Actual??"нет")+". "+state.Error:string.IsNullOrEmpty(state.Actual)?"Выключено":"Активно: "+state.Actual;
                }
            }
            updating=true;mixerStartup.IsChecked=p.StartMixerWithHub;updating=false;
            foreach(var pair in keys){pair.Value.SetResourceReference(BackgroundProperty,"Hub.Surface");pair.Value.SetResourceReference(BorderBrushProperty,"Hub.Border");}
            if(selected!=null)
            {
                string selectedGesture=p.Get(selected);
                foreach(string token in (selectedGesture??"").Split('+')){Border cap;if(keys.TryGetValue(token,out cap))cap.SetResourceReference(BorderBrushProperty,"Hub.Accent");}
            }
        }
        private void ApplyTheme(){Ui.Apply(this,themes.Current,frame,image);}
        private void ThemeChanged(object sender,EventArgs e){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(ApplyTheme));}
        private void BindingsChanged(object sender,EventArgs e){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(Refresh));}
    }
}
