using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using TokenValidator.Utils;


namespace TokenValidator.Models
{
    public class VersionViewModel : INotifyPropertyChanged
    {
        private string _versionInfo;
        public string VersionInfo
        {
            get => _versionInfo;
            set
            {
                _versionInfo = value;
                OnPropertyChanged();
            }
        }

        public VersionViewModel()
        {
            UpdateVersionInfo();
        }

        public void UpdateVersionInfo()
        {
            var version = GetVersion();
            var buildDate = GetBuildDate();

            VersionInfo = $"SCP:SL Token Validator | v{version} | Built: {buildDate:yyyy-MM-dd HH:mm}";
        }


        private string GetVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return "Unkown";
            }
            
        }

        private DateTime GetBuildDate()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = Assembly.GetExecutingAssembly().GetName().Version;

                 if (version.Build == 0 && version.Revision == 0)
                {
                    var filePath = Assembly.GetExecutingAssembly().Location;
                    return File.GetLastWriteTime(filePath);
                }

                return new DateTime(2000, 1, 1).AddDays(version.Build).AddSeconds(version.Revision * 2);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return DateTime.Now;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
