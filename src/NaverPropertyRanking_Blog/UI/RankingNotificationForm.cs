using NaverPropertyRanking_Blog.Models;
using NaverPropertyRanking_Blog.Services;

namespace NaverPropertyRanking_Blog.UI;

public sealed class RankingNotificationForm : Form
{
    private readonly Icon _ownedIcon;
    private readonly int _cascadeIndex;

    public RankingNotificationForm(
        Icon applicationIcon,
        string windowTitle,
        string headline,
        string scope,
        int successCount,
        int failureCount,
        IReadOnlyList<NotificationEvent> events,
        Action openApplication,
        int cascadeIndex = 0)
    {
        _ownedIcon = (Icon)applicationIcon.Clone();
        _cascadeIndex = Math.Max(0, cascadeIndex);
        Icon = _ownedIcon;
        Text = windowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(860, 560);
        Font = new Font("맑은 고딕", 9F);
        BackColor = Color.White;

        var title = new Label
        {
            Text = headline,
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(20, 15, 20, 0),
            Font = new Font("맑은 고딕", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(3, 105, 65)
        };
        var summary = new Label
        {
            Text = $"조회 범위: {scope}   ·   성공 {successCount}건   ·   실패 {failureCount}건   ·   변동 {events.Count}건",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(21, 7, 20, 0),
            ForeColor = failureCount == 0 ? Color.FromArgb(55, 55, 55) : Color.Firebrick
        };
        var details = BuildEventList(events);
        var closeButton = new Button
        {
            Text = "확인",
            Width = 92,
            Height = 34
        };
        var openButton = new Button
        {
            Text = "시스템 열기",
            Width = 112,
            Height = 34
        };
        closeButton.Click += (_, _) => Close();
        openButton.Click += (_, _) =>
        {
            openApplication();
            Close();
        };
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(0, 10, 20, 10),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(openButton);

        var detailPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 0) };
        detailPanel.Controls.Add(details);
        Controls.Add(detailPanel);
        Controls.Add(buttonPanel);
        Controls.Add(summary);
        Controls.Add(title);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    private static Control BuildEventList(IReadOnlyList<NotificationEvent> events)
    {
        if (events.Count == 0)
        {
            return new Label
            {
                Text = "변동 내역이 없습니다.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(248, 250, 249),
                ForeColor = Color.FromArgb(90, 90, 90),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("맑은 고딕", 10F)
            };
        }

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(242, 245, 244),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8)
        };
        foreach (var notificationEvent in events)
            list.Controls.Add(BuildEventCard(notificationEvent));

        void ResizeCards()
        {
            var width = Math.Max(300, list.ClientSize.Width - list.Padding.Horizontal - 4);
            foreach (Control control in list.Controls)
            {
                control.Width = width;
                // 폭이 정해져야 줄바꿈 결과를 알 수 있으므로 높이는 그 다음에 맞춘다.
                if (control is TableLayoutPanel card) card.Height = MeasureCardHeight(card);
            }
        }

        list.ClientSizeChanged += (_, _) => ResizeCards();
        ResizeCards();
        return list;
    }

    /// <summary>카드 최소 높이. 내용이 짧아도 이보다 납작해지지 않게 한다.</summary>
    private const int MinimumCardHeight = 60;

    /// <summary>
    /// 칸마다 줄바꿈된 글이 몇 줄이 되는지 재어 가장 높은 칸에 카드 높이를 맞춘다.
    /// 이렇게 해야 긴 가격변동 문구도 잘리지 않는다.
    /// </summary>
    private static int MeasureCardHeight(TableLayoutPanel card)
    {
        var inner = card.Width - card.Padding.Horizontal;
        if (inner <= 0) return MinimumCardHeight;

        var needed = 0;
        foreach (Control cell in card.Controls)
        {
            if (cell is not Label label || label.Text.Length == 0) continue;

            var column = card.GetColumn(label);
            if (column < 0 || column >= card.ColumnStyles.Count) continue;

            var cellWidth = (int)(inner * card.ColumnStyles[column].Width / 100f)
                            - label.Padding.Horizontal - 2;
            if (cellWidth < 40) continue;

            var size = TextRenderer.MeasureText(
                label.Text,
                label.Font,
                new Size(cellWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            needed = Math.Max(needed, size.Height + label.Padding.Vertical);
        }

        return Math.Max(MinimumCardHeight, needed + card.Padding.Vertical + 6);
    }

    private static Control BuildEventCard(NotificationEvent notificationEvent)
    {
        var card = new TableLayoutPanel
        {
            Height = 78,
            Width = 800,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4),
            Padding = new Padding(8, 2, 8, 2),
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));   // 매물번호
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));   // 매물명
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));   // 거래정보·금액
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13));   // 홍보방식
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));   // 변동내용

        var listingName = string.IsNullOrWhiteSpace(notificationEvent.ListingName)
            ? notificationEvent.ArticleNo
            : notificationEvent.ListingName;
        card.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(notificationEvent.ArticleNo)
                ? "매물번호 없음"
                : notificationEvent.ArticleNo,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Padding = new Padding(5, 0, 5, 0),
            Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 76, 70)
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Text = listingName,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            Padding = new Padding(7, 0, 5, 0),
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40)
        }, 1, 0);
        card.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(notificationEvent.TradeSummary)
                ? "거래정보 없음"
                : notificationEvent.TradeSummary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Padding = new Padding(5, 0, 5, 0),
            ForeColor = Color.FromArgb(65, 65, 65)
        }, 2, 0);
        // 순위가 왜 밀렸는지 함께 보이도록 금액 옆에 홍보방식을 따로 둔다.
        card.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(notificationEvent.VerificationType)
                ? "-"
                : notificationEvent.VerificationType,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Padding = new Padding(5, 0, 5, 0),
            Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(65, 65, 65)
        }, 3, 0);
        card.Controls.Add(new Label
        {
            Text = $"{notificationEvent.Title}\r\n{notificationEvent.Message}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            // 가격변동처럼 긴 문구도 잘리지 않도록 줄바꿈으로 풀어 쓴다.
            AutoEllipsis = false,
            Padding = new Padding(7, 0, 5, 0),
            Font = new Font("맑은 고딕", 10.5F, FontStyle.Bold),
            ForeColor = HighlightColor(notificationEvent.Highlight)
        }, 4, 0);
        return card;
    }

    private static Color HighlightColor(NotificationHighlight highlight) => highlight switch
    {
        NotificationHighlight.RankUp => Color.FromArgb(196, 35, 45),
        NotificationHighlight.RankDown => Color.FromArgb(32, 92, 176),
        NotificationHighlight.Warning => Color.FromArgb(214, 105, 0),
        NotificationHighlight.PriceChange => Color.FromArgb(126, 63, 152),
        NotificationHighlight.NewDuplicate => Color.FromArgb(0, 125, 92),
        _ => Color.FromArgb(55, 55, 55)
    };

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        CenterToScreen();
        var workingArea = Screen.FromControl(this).WorkingArea;
        var offset = _cascadeIndex * 34;
        Location = new Point(
            Math.Clamp(Left + offset, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width)),
            Math.Clamp(Top + offset, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height)));
        BringToFront();
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ownedIcon.Dispose();
        base.Dispose(disposing);
    }
}
