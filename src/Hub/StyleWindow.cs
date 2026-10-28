using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal sealed class StyleWindow : Window
    {
        private readonly ThemeStore store;
        private ThemeProfile profile;
        private ThemeStore editing;
        private readonly ThemeStore mixer,menu;
        private readonly ComboBox target=new ComboBox { Margin=new Thickness(0,4,0,8) };
        private readonly StackPanel selection=new StackPanel();
        private readonly CheckBox inherit=new CheckBox { Content="Наследовать оформление HUB" };
        private bool refreshing; private int generation;
        private readonly Border frame,image;
        private readonly TextBlock note=Ui.Text("Изменения видны сразу и сохраняются автоматически.",11,"Hub.Muted");
        private readonly ComboBox mode=new ComboBox { MinWidth=140,Margin=new Thickness(0,6,0,12) };
        private readonly StackPanel custom=new StackPanel();
        internal StyleWindow(ThemeStore themes)
        {
            store=themes;editing=store;mixer=new ThemeStore(themes.Root,"quiet-mixer");menu=new ThemeStore(themes.Root,"task-menu"); profile=themes.GlobalCurrent; Title="HUB ReFIX — оформление";
            Width=470;Height=750;MinWidth=420;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.CanResizeWithGrip;
            Ui.Styles(this);
            frame=new Border { Margin=new Thickness(18),BorderThickness=new Thickness(1) };
            frame.SetResourceReference(BackgroundProperty,"Hub.Background");frame.SetResourceReference(BorderBrushProperty,"Hub.Border");Content=frame;
            Grid layers=new Grid();frame.Child=layers;
            image=new Border { IsHitTestVisible=false };image.SetResourceReference(BackgroundProperty,"Hub.Image");layers.Children.Add(image);
            Grid shell=new Grid { Margin=new Thickness(20,16,20,16) }; shell.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layers.Children.Add(shell);
            DockPanel title=new DockPanel { Margin=new Thickness(0,0,0,16),Background=Brushes.Transparent };
            Button close=Ui.Button("✕",Close,false);close.Width=32;close.Padding=new Thickness(0);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Ui.Text("Оформление",23,"Hub.Foreground"));
            title.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){try{DragMove();}catch(InvalidOperationException){}};shell.Children.Add(title);
            StackPanel content=new StackPanel();
            content.Children.Add(Ui.Text("Режим оформления",13,"Hub.Foreground"));
            mode.Items.Add("Система");mode.Items.Add("Пользовательская");mode.SelectedIndex=profile.Mode=="Custom"?1:0;content.Children.Add(mode);
            TextBlock hint=Ui.Text("Система использует тему, акцент и контрастность Windows. В пользовательском режиме HUB, Микшер и Меню задач можно настроить отдельно.",12,"Hub.Muted");hint.Margin=new Thickness(0,0,0,16);content.Children.Add(hint);
            content.Children.Add(custom);
            target.Items.Add("Настройка HUB");target.Items.Add("Настройка Микшера");target.Items.Add("Настройка Меню задач");target.SelectedIndex=0;
            System.Windows.Automation.AutomationProperties.SetName(target,"Объект оформления");
            selection.Children.Add(target);selection.Children.Add(inherit);
            TextBlock inheritanceHint=Ui.Text("Расширения по умолчанию повторяют HUB. Изменение параметра включает отдельное оформление. Режим Система един для всех окон.",11,"Hub.Muted");selection.Children.Add(inheritanceHint);
            content.Children.Insert(content.Children.Count-1,selection);
            target.SelectionChanged+=delegate{SelectTarget();};
            inherit.Checked+=delegate{SetInheritance(true);};inherit.Unchecked+=delegate{SetInheritance(false);};
            BuildFields();
            ScrollViewer scroll=new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0) };Grid.SetRow(scroll,1);shell.Children.Add(scroll);
            note.Margin=new Thickness(0,14,0,0);Grid.SetRow(note,2);shell.Children.Add(note);
            mode.SelectionChanged+=delegate
            {
                if(refreshing)return;ThemeProfile global=store.GlobalCurrent;global.Mode=mode.SelectedIndex==1?"Custom":"System";store.Save(global);SelectTarget();
            };
            UpdateSelection();
            store.Changed+=OnThemeChanged;mixer.Changed+=OnThemeChanged;menu.Changed+=OnThemeChanged;
            Closed+=delegate{store.Changed-=OnThemeChanged;mixer.Changed-=OnThemeChanged;menu.Changed-=OnThemeChanged;mixer.Dispose();menu.Dispose();};
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape)Close();};
            SizeChanged+=delegate{layers.Clip=new RectangleGeometry(new Rect(0,0,layers.ActualWidth,layers.ActualHeight),frame.CornerRadius.TopLeft,frame.CornerRadius.TopLeft);Ui.Apply(this,store.Current,frame,image);};
            Ui.Apply(this,store.GlobalCurrent,frame,image);
        }
        private void BuildFields()
        {
            generation++;int fieldsVersion=generation;custom.Children.Clear();
            Heading("Цвета RGB");
            ColorEditor("Фон",delegate{return profile.Background;},delegate(string v){profile.Background=v;});
            ColorEditor("Панели и элементы",delegate{return profile.Surface;},delegate(string v){profile.Surface=v;});
            ColorEditor("Основной текст",delegate{return profile.Foreground;},delegate(string v){profile.Foreground=v;});
            ColorEditor("Вторичный текст",delegate{return profile.Muted;},delegate(string v){profile.Muted=v;});
            ColorEditor("Третичный текст",delegate{return profile.Tertiary;},delegate(string v){profile.Tertiary=v;});
            ColorEditor("Акцент",delegate{return profile.Accent;},delegate(string v){profile.Accent=v;});
            ColorEditor("Обводка",delegate{return profile.Border;},delegate(string v){profile.Border=v;});
            Heading("Прозрачность и свечение");
            Number("Непрозрачность фона",profile.BackgroundOpacity,0,1,"%",delegate(double v){profile.BackgroundOpacity=v;});
            ColorEditor("Цвет свечения",delegate{return profile.Glow;},delegate(string v){profile.Glow=v;});
            Number("Интенсивность свечения",profile.GlowOpacity,0,1,"%",delegate(double v){profile.GlowOpacity=v;});
            Number("Радиус свечения",profile.GlowRadius,0,40,"px",delegate(double v){profile.GlowRadius=v;});
            Number("Скругление углов",profile.CornerRadius,0,24,"px",delegate(double v){profile.CornerRadius=v;});
            Number("Размер текста",profile.FontSize,11,20,"px",delegate(double v){profile.FontSize=v;});
            Heading("Фоновое изображение");
            WrapPanel imageActions=new WrapPanel { Margin=new Thickness(0,8,0,8) };
            imageActions.Children.Add(Ui.Button("Выбрать изображение",PickImage,false));
            Button clear=Ui.Button("Убрать",delegate{profile.ImagePath="";Save();},false);clear.Margin=new Thickness(8,0,0,0);imageActions.Children.Add(clear);custom.Children.Add(imageActions);
            ComboBox fit=new ComboBox { Margin=new Thickness(0,4,0,8) };fit.Items.Add("Заполнить с обрезкой");fit.Items.Add("Вписать целиком");fit.Items.Add("Растянуть");fit.SelectedIndex=profile.ImageFit=="Uniform"?1:profile.ImageFit=="Fill"?2:0;
            fit.SelectionChanged+=delegate{if(fieldsVersion!=generation)return;profile.ImageFit=fit.SelectedIndex==1?"Uniform":fit.SelectedIndex==2?"Fill":"UniformToFill";Save();};custom.Children.Add(fit);
            Number("Видимость изображения",profile.ImageOpacity,0,1,"%",delegate(double v){profile.ImageOpacity=v;});
            Number("Размытие изображения",profile.ImageBlur,0,30,"px",delegate(double v){profile.ImageBlur=v;});
            Number("Горизонтальное положение",profile.ImageX,0,1,"%",delegate(double v){profile.ImageX=v;});
            Number("Вертикальное положение",profile.ImageY,0,1,"%",delegate(double v){profile.ImageY=v;});
            TextBlock imageHint=Ui.Text("Параметры изображения передаются расширениям. В каждом окне сохраняются пропорции и выбранное положение.",11,"Hub.Muted");imageHint.Margin=new Thickness(0,8,0,12);custom.Children.Add(imageHint);
        }
        private ThemeProfile EditableProfile(){return editing==store?store.GlobalCurrent:editing.ScopeOverrideEnabled?editing.ScopedCurrent:editing.Current;}
        private void SelectTarget()
        {
            // Invalidate detached controls before replacing the selected profile.
            generation++;editing=target.SelectedIndex==1?mixer:target.SelectedIndex==2?menu:store;
            profile=EditableProfile();
            BuildFields();UpdateSelection();
        }
        private void UpdateSelection()
        {
            refreshing=true;bool isCustom=store.GlobalCurrent.Mode=="Custom";selection.Visibility=isCustom?Visibility.Visible:Visibility.Collapsed;
            custom.IsEnabled=isCustom;inherit.Visibility=editing==store?Visibility.Collapsed:Visibility.Visible;inherit.IsChecked=editing!=store&&!editing.ScopeOverrideEnabled;refreshing=false;
        }
        private void SetInheritance(bool value)
        {
            if(refreshing||editing==store)return;editing.SetScopeOverrideEnabled(!value);SelectTarget();
        }
        private void Heading(string text)
        {
            TextBlock heading=Ui.Text(text,16,"Hub.Foreground");heading.Margin=new Thickness(0,18,0,8);custom.Children.Add(heading);
        }
        private void Save()
        {
            try{profile.Normalize();editing.Save(profile.Clone());UpdateSelection();note.Text="Изменения применены. Оформление сохраняется автоматически.";}
            catch(Exception ex){note.Text="Не удалось сохранить: "+ex.Message;}
        }
        private void OnThemeChanged(object sender,EventArgs e)
        {
            if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{
                Ui.Apply(this,store.GlobalCurrent,frame,image);
                var next=EditableProfile();var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
                if(serializer.Serialize(next)!=serializer.Serialize(profile)){generation++;profile=next;BuildFields();}
                refreshing=true;mode.SelectedIndex=store.GlobalCurrent.Mode=="Custom"?1:0;refreshing=false;UpdateSelection();
                string error=editing.LastError??store.LastError;note.Text=string.IsNullOrEmpty(error)?"Изменения применены. Оформление сохраняется автоматически.":"Не удалось сохранить оформление: "+error;
            }));
        }
        private void PickImage()
        {
            OpenFileDialog dialog=new OpenFileDialog { Title="Фоновое изображение",Filter="Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Все файлы|*.*",CheckFileExists=true };
            if(dialog.ShowDialog(this)!=true)return;
            try{profile.ImagePath=editing.ImportBackground(dialog.FileName);Save();}
            catch(Exception ex){note.Text="Изображение не загружено: "+ex.Message;}
        }
        private void Number(string label,double value,double min,double max,string unit,Action<double> changed)
        {
            int version=generation;DockPanel heading=new DockPanel { Margin=new Thickness(0,8,0,0) };
            TextBlock amount=Ui.Text("",11,"Hub.Muted");DockPanel.SetDock(amount,Dock.Right);heading.Children.Add(amount);heading.Children.Add(Ui.Text(label,12,"Hub.Foreground"));custom.Children.Add(heading);
            bool percent=unit=="%";
            Action<double> show=delegate(double n){amount.Text=(percent?Math.Round(n*100):Math.Round(n)).ToString(CultureInfo.InvariantCulture)+" "+unit;};
            show(value);Slider slider=new Slider { Minimum=min,Maximum=max,Value=value,SmallChange=percent?.01:1,LargeChange=percent?.1:4,IsMoveToPointEnabled=true };
            System.Windows.Automation.AutomationProperties.SetName(slider,label);
            slider.ValueChanged+=delegate{if(version!=generation)return;show(slider.Value);changed(slider.Value);Save();};custom.Children.Add(slider);
        }
        private void ColorEditor(string label,Func<string> get,Action<string> set)
        {
            bool updating=false;int version=generation;
            StackPanel body=new StackPanel { Margin=new Thickness(20,4,2,10) };
            DockPanel header=new DockPanel();
            Border swatch=new Border { Width=20,Height=20,CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,9,0),Background=ThemeApplier.ColorBrush(get()) };header.Children.Add(swatch);header.Children.Add(Ui.Text(label,12,"Hub.Foreground"));
            Expander group=new Expander { Header=header,Content=body,IsExpanded=label=="Акцент" };custom.Children.Add(group);
            TextBox hex=new TextBox { Text=get(),Margin=new Thickness(0,4,0,8),MaxLength=7 };
            System.Windows.Automation.AutomationProperties.SetName(hex,label+" HEX");body.Children.Add(hex);
            Color color=((SolidColorBrush)ThemeApplier.ColorBrush(get())).Color;
            Slider[] channels=new Slider[3]; byte[] bytes={color.R,color.G,color.B};string[] names={"R","G","B"};
            for(int i=0;i<3;i++)
            {
                int index=i;DockPanel line=new DockPanel();TextBlock component=Ui.Text(names[i],11,"Hub.Muted");component.Width=22;component.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(component);
                TextBlock amount=Ui.Text(bytes[i].ToString(),11,"Hub.Muted");amount.Width=28;amount.TextAlignment=TextAlignment.Right;amount.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(amount,Dock.Right);line.Children.Add(amount);
                Slider slider=new Slider { Minimum=0,Maximum=255,Value=bytes[i],SmallChange=1,LargeChange=16,IsSnapToTickEnabled=true,TickFrequency=1,IsMoveToPointEnabled=true };channels[i]=slider;
                System.Windows.Automation.AutomationProperties.SetName(slider,label+" "+names[i]);
                slider.ValueChanged+=delegate
                {
                    amount.Text=Math.Round(channels[index].Value).ToString();if(updating||version!=generation)return;
                    if(channels[0]==null || channels[1]==null || channels[2]==null)return;
                    string selected=String.Format("#{0:X2}{1:X2}{2:X2}",(byte)channels[0].Value,(byte)channels[1].Value,(byte)channels[2].Value);
                    set(selected);hex.Text=selected;swatch.Background=ThemeApplier.ColorBrush(selected);Save();
                };
                line.Children.Add(slider);body.Children.Add(line);
            }
            Action applyHex=delegate
            {
                if(version!=generation)return;string selected=hex.Text.Trim();if(!selected.StartsWith("#"))selected="#"+selected;
                if(!Regex.IsMatch(selected,"^#[0-9a-fA-F]{6}$")){hex.ToolTip="Введите RGB цвет: #RRGGBB";hex.Text=get();return;}
                selected=selected.ToUpperInvariant();set(selected);swatch.Background=ThemeApplier.ColorBrush(selected);hex.Text=selected;
                Color chosen=((SolidColorBrush)swatch.Background).Color;updating=true;channels[0].Value=chosen.R;channels[1].Value=chosen.G;channels[2].Value=chosen.B;updating=false;Save();
            };
            hex.LostKeyboardFocus+=delegate{applyHex();};hex.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){applyHex();e.Handled=true;}};
        }
    }
}
