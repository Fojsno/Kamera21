using System.Windows;

namespace Kamera21
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
        private void TestLog_Click(object sender, RoutedEventArgs e)
        {
            App.Log("Тестовая запись в лог из кнопки");
            MessageBox.Show("Лог записан. Проверьте файл kamera21.log в папке приложения.");
        }
    }
}