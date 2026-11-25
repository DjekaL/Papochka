namespace Papochka {
    public class FolderData {
        public string Name { get; set; } = "Папочка";
        public double Left { get; set; }
        public double Top { get; set; }
        public List<AppItem> Items { get; set; } = new();

        public string BgColor { get; set; } = "#1E1E1E"; 
        public double BgOpacity { get; set; } = 0.8; 
        public double ExpandedSize { get; set; } = 400;
        public int MiniCols { get; set; } = 1; 
        public int MiniRows { get; set; } = 1;
        public bool QuickLaunchFromMini { get; set; } = false;
        public bool ShowFolderName { get; set; } = true;
        public bool ExpandOnHover { get; set; } = false;
        public int HoverOpenDelayMs { get; set; } = 300;
        public int HoverCloseDelayMs { get; set; } = 400;



    }
}
