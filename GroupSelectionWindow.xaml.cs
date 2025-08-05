using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ymm4_guiequalizer
{
    public partial class GroupSelectionWindow : Window
    {
        public string SelectedGroup { get; private set; } = "";

        public GroupSelectionWindow(string[] groupKeys, string[] groupNames, string currentGroup)
        {
            InitializeComponent();

            var groupOptions = new List<GroupOption>();
            for (int i = 0; i < groupKeys.Length; i++)
            {
                groupOptions.Add(new GroupOption
                {
                    Key = groupKeys[i],
                    DisplayName = groupNames[i]
                });
            }

            GroupListBox.ItemsSource = groupOptions;

            var selectedOption = groupOptions.FirstOrDefault(g => g.Key == currentGroup);
            if (selectedOption != null)
            {
                GroupListBox.SelectedItem = selectedOption;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (GroupListBox.SelectedItem is GroupOption selected)
            {
                SelectedGroup = selected.Key;
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class GroupOption
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}