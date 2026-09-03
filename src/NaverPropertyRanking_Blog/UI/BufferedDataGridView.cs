namespace NaverPropertyRanking_Blog.UI;

internal sealed class BufferedDataGridView : DataGridView
{
    public BufferedDataGridView()
    {
        DoubleBuffered = true;
    }
}
