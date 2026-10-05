using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using HebrewTaskbarWidget.Services;

namespace HebrewTaskbarWidget
{
    /// <summary>
    /// חלונית הודעה מותאמת-עיצוב (קצוות מעוגלים, כפתורים תואמים, יישור RTL
    /// אמיתי) - במקום MessageBox הגולמי של Windows, כדי שכל ההודעות בתוכנה
    /// (אישור הפעלה מחדש של Explorer, "התוכנה כבר פועלת", "אודות" וכו')
    /// ייראו כמו חלק אינטגרלי מממשק התוכנה, לא כמו תיבת דו-שיח מערכתית
    /// גנרית. מחליפה את RtlMessageBox (ששימש קודם לכן) עם אותה חתימת Show
    /// בדיוק, כך שהחלפה בכל מקום שבו נעשה בו שימוש היא פשוטה.
    /// </summary>
    public partial class AppMessageBoxWindow : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;

        // גודל "בינוני" למצב גלילה גדולה - קטן מעט מגודל ברירת המחדל של
        // פאנל ההגדרות עצמו (480x660, ראו SettingsWindow.xaml), כדי שיהיה
        // ברור חזותית שזו חלונית משנית ולא עוד עותק של פאנל ההגדרות.
        private const double LargeModeWidth = 560.0;
        private const double LargeModeHeight = 620.0;
        private const double LargeModeScrollMaxHeight = 460.0;

        private bool _isLargeScrollable;

        private AppMessageBoxWindow(string text, string caption, MessageBoxButton button, MessageBoxImage icon, Window? owner, bool largeScrollable, string? extraButtonText = null)
        {
            InitializeComponent();

            if (owner is not null)
            {
                Owner = owner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            Title = caption;
            TitleText.Text = caption;
            IconText.Text = IconGlyphFor(icon);
            IconText.Visibility = icon == MessageBoxImage.None ? Visibility.Collapsed : Visibility.Visible;
            if (icon is MessageBoxImage.Error or MessageBoxImage.Warning)
            {
                IconText.SetResourceReference(TextBlock.ForegroundProperty, "DangerForegroundBrush");
            }

            _isLargeScrollable = largeScrollable;

            if (largeScrollable)
            {
                // מצב "גלילה גדולה" (למשל "מה חדש בגרסה זו"): במקום להיפתח
                // בגודל שמתאים בדיוק לתוכן (שעלול לצאת ענק אם רשימת השיפורים
                // ארוכה, ואף לחרוג ממסך) - נפתחת בגודל בינוני קבוע, וה-
                // ScrollViewer סביב הטקסט מקבל MaxHeight כדי שרק הוא (לא כל
                // החלונית) יגלול את המשך התוכן שלא נכנס.
                //
                // התוכן הוא Markdown של GitHub (הערות השחרור), ומוצג בעיצוב מלא,
                // כל בלוק בכיוון של השפה שלו - ראו MarkdownRenderer.
                SizeToContent = SizeToContent.Width;
                Height = LargeModeHeight;
                RootBorder.Width = LargeModeWidth;
                RootBorder.MaxWidth = LargeModeWidth;
                MessageScrollViewer.MaxHeight = LargeModeScrollMaxHeight;

                FrameworkElement notes = MarkdownRenderer.Render(text);
                notes.Margin = new Thickness(0, 0, 10, 0); // מרווח מפס הגלילה (משמאל, בחלון מימין לשמאל)
                MessageScrollViewer.Content = notes;
            }
            else
            {
                MessageTextBlock.Text = text;
            }

            BuildButtons(button, extraButtonText);
            ApplyTheme(SettingsService.Current.SettingsPanelDarkMode);
        }

        /// <summary>
        /// מציגה הודעה מודאלית בעיצוב התואם לתוכנה. owner, אם צוין, ממקם
        /// את ההודעה מרכזית ביחס לחלון הקורא ומקשר אליו כחלון-אב (מודאלי).
        /// largeScrollable, אם true, פותח את החלונית בגודל בינוני קבוע עם
        /// גלילה פנימית לתוכן ארוך (ראו הערה למעלה) - מיועד לתוכן ארוך
        /// במיוחד כמו "מה חדש בגרסה זו"; ברירת המחדל (false) משמרת את
        /// ההתנהגות הרגילה (התאמת גודל אוטומטית לתוכן קצר).
        /// </summary>
        public static MessageBoxResult Show(
            string text,
            string caption,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            Window? owner = null,
            bool largeScrollable = false,
            string? extraButtonText = null)
        {
            var dialog = new AppMessageBoxWindow(text, caption, button, icon, owner, largeScrollable, extraButtonText);
            dialog.ShowDialog();
            return dialog._result;
        }

        private static string IconGlyphFor(MessageBoxImage icon)
        {
            // סמלילי Segoe Fluent Icons, כמו בשאר הממשק
            return icon switch
            {
                MessageBoxImage.Error => "",
                MessageBoxImage.Question => "",
                MessageBoxImage.Warning => "",
                MessageBoxImage.Information => "",
                _ => string.Empty,
            };
        }

        private void BuildButtons(MessageBoxButton button, string? extraButtonText = null)
        {
            ButtonsPanel.Children.Clear();

            switch (button)
            {
                case MessageBoxButton.OKCancel:
                    AddButton("אישור", MessageBoxResult.OK, primary: true);
                    AddButton("ביטול", MessageBoxResult.Cancel, primary: false);
                    break;

                case MessageBoxButton.YesNo:
                    AddButton("כן", MessageBoxResult.Yes, primary: true);
                    AddButton("לא", MessageBoxResult.No, primary: false);

                    // extraButtonText: כפתור שלישי אופציונלי (למשל "מה חדש"
                    // בהודעת "עדכון תוכנה זמין") - משתמש ב-MessageBoxResult.Cancel
                    // כערך ה"אות" שלו, כי אינו בשימוש כלל במצב YesNo הרגיל,
                    // כך שאין דו-משמעות מול הכן/לא האמיתיים.
                    if (!string.IsNullOrWhiteSpace(extraButtonText))
                    {
                        AddButton(extraButtonText, MessageBoxResult.Cancel, primary: false);
                    }

                    break;

                case MessageBoxButton.YesNoCancel:
                    AddButton("כן", MessageBoxResult.Yes, primary: true);
                    AddButton("לא", MessageBoxResult.No, primary: false);
                    AddButton("ביטול", MessageBoxResult.Cancel, primary: false);
                    break;

                default:
                    AddButton("אישור", MessageBoxResult.OK, primary: true);
                    break;
            }
        }

        private void AddButton(string content, MessageBoxResult result, bool primary)
        {
            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource(primary ? "PrimaryActionButtonStyle" : "ActionButtonStyle"),
                Margin = new Thickness(5, 0, 5, 0),
                IsDefault = primary,
            };

            button.Click += (_, _) =>
            {
                _result = result;
                Close();
            };

            ButtonsPanel.Children.Add(button);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        /// <summary>מחילה את הפלטה המשותפת, לפי מצב התצוגה של פאנל ההגדרות.</summary>
        private void ApplyTheme(bool dark)
        {
            AppTheme.Apply(Resources, dark);
        }
    }
}
