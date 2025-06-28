using System;

namespace AppxInstaller.Models
{
    public enum PackageType
    {
        MainApp,
        Dependency,
        Bundle
    }

    public class PackageFileModel
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public PackageType Type { get; set; }
        public bool IsSelected { get; set; }
        public bool ShowCheckbox { get; set; }

        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case PackageType.Dependency:
                        return "DEP";
                    case PackageType.Bundle:
                        return "BUNDLE";
                    case PackageType.MainApp:
                        return "APP";
                    default:
                        return "FILE";
                }
            }
        }

        public string TypeColor
        {
            get
            {
                switch (Type)
                {
                    case PackageType.Dependency:
                        return "#FF2196F3";
                    case PackageType.Bundle:
                        return "#FF9C27B0";
                    case PackageType.MainApp:
                        return "#FF4CAF50";
                    default:
                        return "#FF757575";
                }
            }
        }
    }
}