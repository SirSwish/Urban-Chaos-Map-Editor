using System.Windows;

namespace UrbanChaosMapEditor.Views
{
    public partial class PrimPropertiesDialog : Window
    {
        // Bindable properties (bit wrappers)\
        public bool OnFloor { get; set; }
        public bool Searchable { get; set; }
        public bool NotOnPsx { get; set; }
        public bool Damaged { get; set; }
        public bool Warehouse { get; set; }
        public bool HiddenItem { get; set; }
        public bool Reserved1 { get; set; }
        public bool Reserved2 { get; set; }

        public bool IsInside { get; set; }

        // Final values for dialog result
        public byte FlagsValue { get; private set; }
        public byte InsideIndexValue { get; private set; }

        // Constructor
        public PrimPropertiesDialog(byte flags, byte insideIndex)
        {
            InitializeComponent();

            // Set the flags and inside index values based on the passed values
            OnFloor = (flags & (1 << 0)) != 0;
            Searchable = (flags & (1 << 1)) != 0;
            NotOnPsx = (flags & (1 << 2)) != 0;
            Damaged = (flags & (1 << 3)) != 0;
            Warehouse = (flags & (1 << 4)) != 0;
            HiddenItem = (flags & (1 << 5)) != 0;
            Reserved1 = (flags & (1 << 6)) != 0;
            Reserved2 = (flags & (1 << 7)) != 0;

            IsInside = insideIndex != 0;

            // Set the DataContext for binding
            DataContext = this;
        }

        // OK button click handler
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Calculate the flags based on checkbox values
            byte flags = 0;
            if (OnFloor) flags |= 1 << 0;
            if (Searchable) flags |= 1 << 1;
            if (NotOnPsx) flags |= 1 << 2;
            if (Damaged) flags |= 1 << 3;
            if (Warehouse) flags |= 1 << 4;
            if (HiddenItem) flags |= 1 << 5;
            if (Reserved1) flags |= 1 << 6;
            if (Reserved2) flags |= 1 << 7;

            FlagsValue = flags;
            InsideIndexValue = (byte)(IsInside ? 1 : 0);

            // Return the dialog result to signal success
            DialogResult = true;
        }

        // Cancel button click handler (optional)
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Close the dialog without making any changes
            DialogResult = false;
        }
    }
}
