using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;

namespace HebrewTaskbarWidget
{
    /// <summary>
    /// חלונית קטנה לעריכת התאמה אישית לזמן בודד - נפתחת בלחיצה על שם הזמן
    /// ברשימת "אילו זמנים להציג" (לשונית "מיקום וזמנים"). מאפשרת: שם מותאם
    /// אישית, דריסת שיטת חישוב (לזמנים "רגילים"), הגדרות מיוחדות ל"הדלקת
    /// נרות"/"צאת הכוכבים", והוספת/הסרת "שורת זמן כפולה" (אותו זמן פעם
    /// נוספת, בשיטת חישוב אחרת - ראו AppSettings.ZmanDuplicateRow). שתי
    /// השורות (הראשית והכפולה, כשיש) מוצגות בסימטריה - לכל אחת סמליל מחיקה
    /// משלה; מחיקת אחת מהן משאירה את השנייה כתצורה החדשה, היחידה, של הזמן.
    ///
    /// כל ההגדרות כאן משפיעות על כל מקום בתוכנה שמציג/מתריע על הזמן הזה
    /// (לוח הזמנים, כללי התראה) - ראו ZmanEntry.Key מול ZmanEntry.DisplayName.
    /// </summary>
    public partial class ZmanEditDialog : Window
    {
        private readonly string _baseZmanName;
        private readonly bool _isCandleLighting;
        private readonly bool _isTzeit;
        private readonly ZmanCalculationMethod _globalDefaultMethod;
        private readonly Func<ZmanCalculationMethod, DateTime?> _computeTimeForMethod;

        private ZmanDuplicateRow? _duplicate;

        /// <summary>כמה דקות מדליקים לפי הלוח שנבחר כשיטה הכללית, או null אם השיטה הכללית אינה לוח.</summary>
        private readonly int? _luachCandleLightingMinutes;

        /// <summary>השם שמוצג כשאין שם מותאם אישית - שמירה בלי לשנותו אינה יוצרת שם מותאם.</summary>
        private readonly string _defaultDisplayName;

        public string? ResultCustomName { get; private set; }
        public ZmanCalculationMethod? ResultMethodOverride { get; private set; }
        public int ResultCandleLightingMinutes { get; private set; }
        public bool ResultCandleLightingByLuach { get; private set; }
        public int? ResultTzeitMinutesOverride { get; private set; }
        public ZmanDuplicateRow? ResultDuplicateRow { get; private set; }

        public ZmanEditDialog(
            string baseZmanName,
            string? currentCustomName,
            ZmanCalculationMethod? currentMethodOverride,
            int currentCandleLightingMinutes,
            bool currentCandleLightingByLuach,
            int? luachCandleLightingMinutes,
            int? currentTzeitMinutesOverride,
            ZmanDuplicateRow? existingDuplicate,
            ZmanCalculationMethod globalDefaultMethod,
            Func<ZmanCalculationMethod, DateTime?> computeTimeForMethod)
        {
            InitializeComponent();

            // צבעים כמו בחלון האב (גם אם מצב התצוגה שונה שם ועוד לא נשמר)
            AppTheme.Apply(Resources, SettingsService.Current.SettingsPanelDarkMode);
            Loaded += (_, _) => AppTheme.CopyPalette(Owner?.Resources, Resources);

            _baseZmanName = baseZmanName;
            _isCandleLighting = baseZmanName == ZmanimCalendar.NameCandleLighting;
            _isTzeit = baseZmanName == ZmanimCalendar.NameTzeitHakochavim;
            _globalDefaultMethod = globalDefaultMethod;
            _computeTimeForMethod = computeTimeForMethod;
            _luachCandleLightingMinutes = luachCandleLightingMinutes;
            _duplicate = existingDuplicate is null ? null : new ZmanDuplicateRow
            {
                Id = existingDuplicate.Id,
                BaseZmanName = existingDuplicate.BaseZmanName,
                CustomName = existingDuplicate.CustomName,
                Method = existingDuplicate.Method,
            };

            // בלי שם מותאם מוצג השם שמתאים לשיטה (בלי "16.1°" בלוח אור החיים, למשל).
            _defaultDisplayName = ZmanimCalendar.GetSettingsDisplayName(baseZmanName, currentMethodOverride ?? globalDefaultMethod);
            string displayNameForTitle = string.IsNullOrWhiteSpace(currentCustomName) ? _defaultDisplayName : currentCustomName!;
            TitleText.Text = $"עריכת \"{displayNameForTitle}\"";
            CustomNameTextBox.Text = currentCustomName ?? _defaultDisplayName;

            if (_isCandleLighting)
            {
                MethodPanel.Visibility = Visibility.Collapsed;
                TzeitMinutesPanel.Visibility = Visibility.Collapsed;
                DuplicateSectionPanel.Visibility = Visibility.Collapsed;
                PrimaryMethodText.Visibility = Visibility.Collapsed;
                PrimaryDeleteButton.Visibility = Visibility.Collapsed;
                CandleLightingMinutesPanel.Visibility = Visibility.Visible;
                CandleLightingMinutesTextBox.Text = currentCandleLightingMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // האפשרות "לפי הלוח" רלוונטית רק כשהשיטה הכללית היא לוח.
                bool luachSelected = luachCandleLightingMinutes is not null;
                CandleLightingByLuachCheckBox.Visibility = luachSelected ? Visibility.Visible : Visibility.Collapsed;
                CandleLightingByLuachText.Visibility = luachSelected ? Visibility.Visible : Visibility.Collapsed;
                CandleLightingByLuachText.Text = luachSelected
                    ? $"ב{ZmanimMethods.Label(globalDefaultMethod)} מדליקים כאן {luachCandleLightingMinutes} דקות לפני השקיעה."
                    : string.Empty;
                CandleLightingByLuachCheckBox.IsChecked = currentCandleLightingByLuach;
                UpdateCandleLightingManualPanel();
                PrimaryTimeText.Text = FormatTimeOrDash(_computeTimeForMethod(_globalDefaultMethod));
            }
            else
            {
                CandleLightingMinutesPanel.Visibility = Visibility.Collapsed;
                MethodPanel.Visibility = Visibility.Visible;

                MethodComboBox.Items.Add(new ComboBoxItem { Content = $"השיטה הכללית ({ZmanimMethods.Label(globalDefaultMethod)})" });
                foreach (ZmanCalculationMethod method in ZmanimMethods.All)
                {
                    MethodComboBox.Items.Add(new ComboBoxItem { Content = ZmanimMethods.Label(method) });
                    DuplicateMethodComboBox.Items.Add(new ComboBoxItem { Content = ZmanimMethods.Label(method) });
                }

                MethodComboBox.SelectedIndex = currentMethodOverride is ZmanCalculationMethod overrideMethod
                    ? 1 + IndexOfMethod(overrideMethod)
                    : 0;

                TzeitMinutesPanel.Visibility = _isTzeit ? Visibility.Visible : Visibility.Collapsed;
                if (_isTzeit)
                {
                    TzeitMinutesTextBox.Text = currentTzeitMinutesOverride?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }

                RefreshDuplicateUi();
            }
        }

        private static string FormatTimeOrDash(DateTime? time) =>
            "זמן נוכחי: " + (time.HasValue ? AppTimeService.FormatZmanTime(time.Value) : "—");

        private static int IndexOfMethod(ZmanCalculationMethod method) =>
            Math.Max(0, ZmanimMethods.All.ToList().IndexOf(method));

        /// <summary>מתרגם את הבחירה ב-MethodComboBox (0 = השיטה הכללית, אחריה כל השיטות לפי הסדר) לשיטה בפועל.</summary>
        private ZmanCalculationMethod EffectivePrimaryMethod()
        {
            int index = MethodComboBox.SelectedIndex - 1;
            return index >= 0 && index < ZmanimMethods.All.Count ? ZmanimMethods.All[index] : _globalDefaultMethod;
        }

        /// <summary>השיטה שמוצעת לשורה כפולה: הראשונה ברשימה שאינה השיטה של השורה הראשית.</summary>
        private static ZmanCalculationMethod FirstOtherMethod(ZmanCalculationMethod primary)
        {
            foreach (ZmanCalculationMethod method in ZmanimMethods.All)
            {
                if (method != primary)
                {
                    return method;
                }
            }

            return primary;
        }

        private static string MethodLabel(ZmanCalculationMethod method) => ZmanimMethods.Label(method);

        private void UpdateCandleLightingManualPanel()
        {
            bool byLuach = _luachCandleLightingMinutes is not null && CandleLightingByLuachCheckBox.IsChecked == true;
            CandleLightingManualPanel.IsEnabled = !byLuach;
        }

        private void CandleLightingByLuachCheckBox_CheckedChanged(object sender, RoutedEventArgs e) => UpdateCandleLightingManualPanel();

        // מונע מ-DuplicateToggle_CheckedChanged להגיב לעדכון התכנותי של
        // DuplicateToggle.IsChecked בתוך RefreshDuplicateUi עצמה - כדי לא
        // ליצור מעגליות/כפילות מיותרת.
        private bool _suppressToggleEvent;

        private void RefreshDuplicateUi()
        {
            _suppressToggleEvent = true;
            DuplicateToggle.IsChecked = _duplicate is not null;
            _suppressToggleEvent = false;

            bool hasDuplicate = _duplicate is not null;
            DuplicateDetailsPanel.Visibility = hasDuplicate ? Visibility.Visible : Visibility.Collapsed;

            // כשיש כפילה, שתי השורות (הראשית והכפולה) מוצגות בסימטריה - לכל
            // אחת תווית שיטה משלה וסמליל מחיקה משלה. בלי כפילה, אלה מוחבאים
            // (השורה הראשית היא היחידה, אין צורך להסביר "שיטה: X" בנפרד).
            PrimaryMethodText.Visibility = hasDuplicate ? Visibility.Visible : Visibility.Collapsed;
            PrimaryDeleteButton.Visibility = hasDuplicate ? Visibility.Visible : Visibility.Collapsed;

            PrimaryMethodText.Text = "שיטת חישוב: " + MethodLabel(EffectivePrimaryMethod());
            PrimaryTimeText.Text = FormatTimeOrDash(_computeTimeForMethod(EffectivePrimaryMethod()));

            if (hasDuplicate)
            {
                DuplicateNameTextBox.Text = _duplicate!.CustomName;
                _suppressDuplicateMethodEvent = true;
                DuplicateMethodComboBox.SelectedIndex = IndexOfMethod(_duplicate.Method);
                _suppressDuplicateMethodEvent = false;
                DuplicateTimeText.Text = FormatTimeOrDash(_computeTimeForMethod(_duplicate.Method));
            }
        }

        /// <summary>אם השורה הראשית עוברת לשיטה של הכפולה, הכפולה עוברת לשיטה אחרת - כדי שלא יהיו שתי שורות זהות.</summary>
        private void MethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_duplicate is not null && _duplicate.Method == EffectivePrimaryMethod())
            {
                _duplicate.Method = FirstOtherMethod(EffectivePrimaryMethod());
                _suppressDuplicateMethodEvent = true;
                DuplicateMethodComboBox.SelectedIndex = IndexOfMethod(_duplicate.Method);
                _suppressDuplicateMethodEvent = false;
            }

            // גם בלי כפילה, מעדכנים את תצוגת הזמן הראשית - היא רלוונטית תמיד
            // (לא רק כשיש כפילה), למרות שהתוויות הנלוות (PrimaryMethodText/
            // PrimaryDeleteButton) מוצגות רק כשיש כפילה.
            PrimaryTimeText.Text = FormatTimeOrDash(_computeTimeForMethod(EffectivePrimaryMethod()));

            if (_duplicate is not null)
            {
                DuplicateTimeText.Text = FormatTimeOrDash(_computeTimeForMethod(_duplicate.Method));
                PrimaryMethodText.Text = "שיטת חישוב: " + MethodLabel(EffectivePrimaryMethod());
            }
        }

        private bool _suppressDuplicateMethodEvent;

        /// <summary>בחירת שיטה לשורה הכפולה. שיטה זהה לזו של השורה הראשית נדחית (חוזרים לבחירה הקודמת).</summary>
        private void DuplicateMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDuplicateMethodEvent || _duplicate is null || DuplicateMethodComboBox.SelectedIndex < 0)
            {
                return;
            }

            ZmanCalculationMethod selected = ZmanimMethods.All[DuplicateMethodComboBox.SelectedIndex];
            if (selected == EffectivePrimaryMethod())
            {
                _suppressDuplicateMethodEvent = true;
                DuplicateMethodComboBox.SelectedIndex = IndexOfMethod(_duplicate.Method);
                _suppressDuplicateMethodEvent = false;
                return;
            }

            // שם שהוצע אוטומטית מתעדכן לשיטה החדשה; שם שהמשתמש כתב נשאר.
            string oldSuggestion = SuggestedDuplicateName(_duplicate.Method);
            _duplicate.Method = selected;
            if (DuplicateNameTextBox.Text.Trim() == oldSuggestion)
            {
                DuplicateNameTextBox.Text = SuggestedDuplicateName(selected);
            }

            DuplicateTimeText.Text = FormatTimeOrDash(_computeTimeForMethod(selected));
        }

        private string SuggestedDuplicateName(ZmanCalculationMethod method)
        {
            string baseDisplayName = string.IsNullOrWhiteSpace(CustomNameTextBox.Text) ? _baseZmanName : CustomNameTextBox.Text.Trim();
            return $"{ZmanimCalendar.GetPopupDisplayName(baseDisplayName)} ({ZmanimMethods.ShortLabel(method)})";
        }

        /// <summary>הפעלת/כיבוי המתג "הוסף שורת זמן כפולה" - ראו הערה מפורטת ב-AppSettings.ZmanDuplicateRow.</summary>
        private void DuplicateToggle_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressToggleEvent)
            {
                return;
            }

            if (DuplicateToggle.IsChecked == true)
            {
                ZmanCalculationMethod otherMethod = FirstOtherMethod(EffectivePrimaryMethod());

                _duplicate = new ZmanDuplicateRow
                {
                    BaseZmanName = _baseZmanName,
                    CustomName = SuggestedDuplicateName(otherMethod),
                    Method = otherMethod,
                };
            }
            else
            {
                _duplicate = null;
            }

            RefreshDuplicateUi();
        }

        /// <summary>מוחקת את השורה ה"כפולה" (השנייה) - השורה הראשית נשארת כפי שהיא, ללא שינוי.</summary>
        private void DeleteDuplicateButton_Click(object sender, RoutedEventArgs e)
        {
            _duplicate = null;
            RefreshDuplicateUi();
        }

        /// <summary>
        /// מוחקת את השורה ה"ראשית" - מקדמת את הכפילה להיות השורה היחידה/
        /// החדשה (שם ושיטה שלה הופכים לשם/שיטה הראשיים), בדיוק כאילו זו
        /// הייתה ההגדרה מההתחלה. ראו דרישה: "החלונית חוזרת להיראות כמו
        /// שהיה לפני הוספת הזמן הכפול, כששורת הזמן שנותרה היא זו שמוצגת".
        /// </summary>
        private void PrimaryDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_duplicate is null)
            {
                return;
            }

            CustomNameTextBox.Text = _duplicate.CustomName;
            ZmanCalculationMethod promotedMethod = _duplicate.Method;
            _duplicate = null;
            MethodComboBox.SelectedIndex = 1 + IndexOfMethod(promotedMethod);
            RefreshDuplicateUi();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string customName = CustomNameTextBox.Text.Trim();
            ResultCustomName = (string.IsNullOrWhiteSpace(customName) || customName == _baseZmanName || customName == _defaultDisplayName) ? null : customName;

            if (_isCandleLighting)
            {
                int minutes = ParseClampedInt(CandleLightingMinutesTextBox.Text, fallback: 40, min: 0, max: 60);
                ResultCandleLightingMinutes = minutes;
                ResultCandleLightingByLuach = CandleLightingByLuachCheckBox.IsChecked == true;
            }
            else
            {
                ResultMethodOverride = MethodComboBox.SelectedIndex > 0 ? EffectivePrimaryMethod() : null;

                if (_isTzeit)
                {
                    string tzeitText = TzeitMinutesTextBox.Text.Trim();
                    ResultTzeitMinutesOverride = string.IsNullOrEmpty(tzeitText)
                        ? null
                        : ParseClampedInt(tzeitText, fallback: 0, min: 0, max: 999);
                }

                if (_duplicate is not null)
                {
                    _duplicate.CustomName = string.IsNullOrWhiteSpace(DuplicateNameTextBox.Text)
                        ? _duplicate.CustomName
                        : DuplicateNameTextBox.Text.Trim();
                }

                ResultDuplicateRow = _duplicate;
            }

            DialogResult = true;
            Close();
        }

        private static int ParseClampedInt(string text, int fallback, int min, int max)
        {
            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value))
            {
                return fallback;
            }

            return Math.Clamp(value, min, max);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
