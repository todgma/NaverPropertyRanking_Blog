using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NaverPropertyRanking_Blog.Models;

namespace NaverPropertyRanking_Blog.Services;

/// <summary>
/// 아실에서 매물번호로 동·호를 읽어 온다.
///
/// 아실은 두 번 물어봐야 한다.
///  1) 목록(등록매물·등록종료)에서 네이버 매물번호로 찾아 아실 매물번호(mm_uid)를 얻고
///  2) 그 번호로 상세 화면을 열어 매물명에서 동·호를 읽는다.
/// 로그인 주소가 계정마다 다르지만 매물 화면은 realty.asil.kr로 공통이다.
/// </summary>
public sealed class AsilDongHoClient : IDongHoLookup
{
    // 상세 화면. 매물명에 동·호가 함께 들어 있어 재등록 폼보다 읽기 쉽다.
    private const string DetailUrl = "https://realty.asil.kr/mmc/memul/memulStep.asp";

    /// <summary>목록·상세 화면이 함께 요구하는 기본 조건. 화면이 보내는 값과 같다.</summary>
    private const string BaseQuery =
        "s_step=&url=&newimgChk=&currentpage=1&s_orderby=&s_orderby2=&s_rlsttype_cd=" +
        "&s_dealtype_cd=&VRFC_TYPE=&s_area=&DONG_NM=&ADR_HO=&ADR_BUNJI=&schSpcSel=" +
        "&schminSpc=&schmaxSpc=&s_startdate=&s_enddate=&s_proc=" +
        "&s_viewCount=20&excel_flag=&srch_trash_flag=&srch_mm_view=N&copy_flag=";

    /// <summary>
    /// 매물이 있을 수 있는 목록. 두 화면 모두 매물번호 입력란 이름이 s_mm_uid로 같다.
    /// 등록매물에서 못 찾으면 등록종료 목록에서 한 번 더 찾는다.
    /// asil_mm_flag=A는 아실에 등록한 매물만 본다는 뜻이다. 다른 CP 매물은 여기서 찾지 않는다.
    /// </summary>
    private static readonly (string Name, string Url, string Extra)[] ListPages =
    [
        ("등록매물", "https://realty.asil.kr/mmc/memul/memulList.asp",
            "asil_mm_flag=A&srch_order_flag=1&page_chart=O"),
        ("등록종료", "https://realty.asil.kr/mmc/memul/memulendlist.asp",
            "asil_mm_flag=A&srch_order_flag=3&page_chart=")
    ];

    /// <summary>목록에서 상세로 넘어가는 링크에 들어 있는 아실 매물번호.</summary>
    private static readonly Regex AsilNoPattern = new(
        @"[?&]mm_uid=(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>콤보에서 선택된 항목. 숨은 입력란이 비었을 때만 쓴다.</summary>
    private static readonly Regex SelectedOptionPattern = new(
        @"<option[^>]*\bselected\b[^>]*>(?<text>.*?)</option>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DigitPattern = new(@"\d+", RegexOptions.Compiled);

    private readonly CpAccount _account;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private bool _loggedIn;
    private bool _disposed;

    public string CpName => "아실";

    static AsilDongHoClient()
    {
        // 오래된 화면이 EUC-KR로 오는 경우가 있어 코드페이지를 등록해 둔다.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public AsilDongHoClient(CpAccount account, HttpMessageHandler? handler = null)
    {
        _account = account;
        handler ??= new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36");
    }

    public async Task<CpLoginTestResult> EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loggedIn) return CpLoginTestResult.Ok("이미 로그인되어 있습니다.");
            var site = CpSite.Find(_account.CpValue);
            if (site is null) return CpLoginTestResult.Fail($"지원하지 않는 CP입니다: {_account.CpValue}");

            var result = await CpLoginTester
                .TestWithClientAsync(_client, site, _account, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success) return result;

            _loggedIn = true;
            return result;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    public async Task<DongHo> GetDongHoAsync(string articleNo, CancellationToken cancellationToken)
    {
        if (!_loggedIn || string.IsNullOrWhiteSpace(articleNo)) return DongHo.Empty;

        try
        {
            var number = articleNo.Trim();
            var search = Uri.EscapeDataString(number);

            // 등록매물에 없으면 등록종료 목록도 본다. 종료된 매물도 동·호는 그대로 남아 있다.
            string? asilNo = null;
            foreach (var (name, url, extra) in ListPages)
            {
                var listHtml = await GetAsync(
                    $"{url}?{BaseQuery}&{extra}&mm_uid=&s_mm_uid={search}",
                    cancellationToken).ConfigureAwait(false);
                if (listHtml is null) continue;

                asilNo = ParseAsilArticleNo(listHtml, number);
                CpLoginTrace.Write($"아실 {name} 조회 · 매물번호 {number} · 아실번호 {(asilNo ?? "없음")}");
                if (asilNo is not null) break;
            }
            // 아실에 없는 매물은 그냥 넘어간다. 다른 CP 계정이 있으면 그쪽에서 채운다.
            if (asilNo is null) return DongHo.Empty;

            var detailHtml = await GetAsync(
                $"{DetailUrl}?{BaseQuery}&asil_mm_flag=&srch_order_flag=1&page_chart=&mm_uid={asilNo}&s_mm_uid={search}",
                cancellationToken).ConfigureAwait(false);
            if (detailHtml is null) return DongHo.Empty;

            var value = ParseDongHo(detailHtml);
            CpLoginTrace.Write($"아실 상세 조회 · 아실번호 {asilNo} · 동 [{value.Dong}] 호 [{value.Ho}]");
            return value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return DongHo.Empty;
        }
    }

    /// <summary>
    /// 목록에서 아실 매물번호를 찾는다.
    /// 네이버 매물번호로 검색했으니 결과는 한 건이고, 상세 링크에 번호가 들어 있다.
    /// 검색한 번호 자신은 건너뛴다.
    /// </summary>
    public static string? ParseAsilArticleNo(string html, string articleNo)
    {
        foreach (Match match in AsilNoPattern.Matches(html ?? string.Empty))
        {
            var value = match.Groups["value"].Value;
            if (value.Length == 0) continue;
            if (string.Equals(value, articleNo, StringComparison.Ordinal)) continue;
            return value;
        }
        return null;
    }

    /// <summary>
    /// 상세 화면에서 동·호를 읽는다.
    /// 매물명 칸에 "단지명 104동 1301호" 형태로 함께 들어 있다.
    /// 단지명에는 숫자가 붙은 동 표기가 없어 뒤쪽 동·호만 정확히 걸린다.
    ///
    /// 등록 화면이 대신 열리는 경우를 대비해 입력란도 한 번 더 본다.
    /// 그 화면의 콤보(bld_no)는 건물 일련번호라 동 번호가 아니므로 숨은 입력란을 먼저 쓴다.
    /// </summary>
    public static DongHo ParseDongHo(string html)
    {
        var text = html ?? string.Empty;

        var fromName = DongHoParser.ParseAddress(ReadTableValue(text, "매물명"));
        if (fromName.HasValue) return fromName;

        var dong = ReadInputValue(text, "dong_nm");
        if (dong.Length == 0) dong = ReadSelectedOption(text, "bld_no");
        var ho = ReadInputValue(text, "adr_ho");
        return new DongHo(NormalizeDong(dong), NormalizeHo(ho));
    }

    /// <summary>상세 표에서 항목 이름(예: 매물명) 바로 뒤에 오는 값을 읽는다.</summary>
    private static string ReadTableValue(string html, string header)
    {
        var match = Regex.Match(
            html,
            $@"<th[^>]*>\s*{Regex.Escape(header)}\s*</th>\s*<td[^>]*>(?<value>.*?)</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? DongHoParser.Normalize(match.Groups["value"].Value) : string.Empty;
    }

    /// <summary>이름이 name인 콤보에서 선택된 항목의 값을 읽는다.</summary>
    private static string ReadSelectedOption(string html, string name)
    {
        var select = Regex.Match(
            html,
            $@"<select[^>]*\bname\s*=\s*[""']{Regex.Escape(name)}[""'][^>]*>(?<body>.*?)</select>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!select.Success) return string.Empty;

        var option = SelectedOptionPattern.Match(select.Groups["body"].Value);
        return option.Success ? DongHoParser.Normalize(option.Groups["text"].Value) : string.Empty;
    }

    /// <summary>이름이 name인 입력란의 값을 읽는다.</summary>
    private static string ReadInputValue(string html, string name)
    {
        var input = Regex.Match(
            html,
            $@"<input[^>]*\bname\s*=\s*[""']{Regex.Escape(name)}[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!input.Success) return string.Empty;

        var value = Regex.Match(input.Value, @"\bvalue\s*=\s*[""'](?<value>[^""']*)[""']", RegexOptions.IgnoreCase);
        return value.Success ? WebUtility.HtmlDecode(value.Groups["value"].Value).Trim() : string.Empty;
    }

    /// <summary>'103'처럼 숫자만 오면 '103동' 형태로 맞춘다.</summary>
    private static string NormalizeDong(string value) => AppendSuffix(value, '동');

    /// <summary>'702'처럼 숫자만 오면 '702호' 형태로 맞춘다.</summary>
    private static string NormalizeHo(string value) => AppendSuffix(value, '호');

    private static string AppendSuffix(string value, char suffix)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;
        if (text[^1] == suffix) return text;
        return DigitPattern.IsMatch(text) ? text + suffix : string.Empty;
    }

    private async Task<string?> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"', '\'');
        var body = Decode(bytes, charset);

        // 로그인이 풀리면 매물 화면 대신 로그인 화면이 돌아온다.
        if (body.Contains("login.jsp", StringComparison.OrdinalIgnoreCase))
        {
            _loggedIn = false;
            return null;
        }
        return body;
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset).GetString(bytes); }
            catch (ArgumentException) { /* 모르는 이름이면 아래로 넘어간다. */ }
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loginGate.Dispose();
        _client.Dispose();
    }
}
