namespace CodexMeter;

/// <summary>Shared two-column card layout. Row heights are physical pixels.</summary>
sealed class MetadataTable : TableLayoutPanel
{
    public MetadataTable()
    {
        Dock = DockStyle.Fill;
        ColumnCount = 2;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        GrowStyle = TableLayoutPanelGrowStyle.AddRows;
    }
}
