using System;
using System.Windows;

namespace RVTuk.UI.Shared.Themes
{
    /// <summary>The dark theme, loaded from whichever assembly this file is compiled into.</summary>
    public class DarkThemeDictionary : ResourceDictionary
    {
        public DarkThemeDictionary()
        {
            var name = typeof(DarkThemeDictionary).Assembly.GetName().Name;
            Source = new Uri("/" + name + ";component/Shared/Themes/DarkTheme.xaml", UriKind.Relative);
        }
    }
}
