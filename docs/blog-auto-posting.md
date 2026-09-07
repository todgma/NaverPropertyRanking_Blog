# 매물 자동 포스팅 설계서 (OpenAI + Google Blogger)

내 매물 데이터를 OpenAI로 블로그 글로 만들어 구글 블로그(Blogger/블로그스팟)에 자동 발행하는 기능의 설계 문서다.
`NaverPropertyRanking_Blog` 프로젝트에 붙이는 것을 전제로 한다.

- 작성일: 2026-09-07
- 대상 프로젝트: `D:\Source\NaverPropertyRanking_Blog` (.NET 8 / WinForms)
- 벤치마크: 빠글 AI (bbagle.ai)

---

## 1. 벤치마크 분석 — 빠글 AI가 하는 일

[빠글 AI 소개 페이지](https://possible-element-35a.notion.site/AI-225471a8764980d4985bcd0ed9ff874b)에서 확인한 구조다.

| 단계 | 빠글 AI | 우리 프로젝트에 대응시키면 |
|---|---|---|
| 입력 | 사용자가 **키워드** 입력 | 이미 갖고 있는 **매물 데이터**(`Listing`) — 키워드 발굴 단계가 필요 없다 |
| 소재 발굴 | 구글·네이버·다음 인기 검색어 분석해 키워드 추천 | 불필요. 단지명·지역명이 곧 키워드다 |
| 본문 생성 | AI가 SEO 구조(제목/소제목/본문/태그/링크)로 작성 | OpenAI Responses API + 구조화된 출력 |
| 이미지 | AI 이미지 생성 | 선택. Blogger API 제약 있음 (§5.3) |
| 채널 연동 | 워드프레스(앱 패스워드) / 블로그스팟(구글 OAuth) / 티스토리 | 블로그스팟만 — OAuth 2.0 |
| 발행 | 단건등록 / 대량등록 자동포스팅 | 단건(매물 1건) / 대량(선택 매물 N건) |

**핵심 차이점**: 빠글 AI는 "키워드 → 없는 내용을 지어냄" 구조라 저품질 판정 위험이 크다.
우리는 **실제 보유 매물이라는 1차 데이터**가 있으므로, AI에게 창작이 아니라 **정형 데이터의 서술**을 시키는 쪽이
품질·법적 안전성 모두 유리하다. 이 문서는 그 전제로 설계했다.

빠글 AI 샘플 글에서 가져올 만한 형식적 요소:

- 도입부 질문형 후킹 + 친근한 존댓말 구어체
- `📋 목차` 블록 (앵커 링크)
- 소제목 3~7개로 분할, 비교는 표로
- 말미에 요약 + CTA

---

## 2. 전체 파이프라인

```
[1] 매물 선택          MainForm 그리드에서 체크 → 또는 자동 스케줄
        │                (기존 Listing / RankingResult 재사용)
        ▼
[2] 사실 카드 생성      Listing → BlogPostFacts (검증된 값만 추출)
        │                ★ AI에 넘기기 전에 여기서 법정 명시사항 채움
        ▼
[3] 본문 생성          OpenAI Responses API + Structured Outputs
        │                → { title, summary, labels[], sections[] }
        ▼
[4] HTML 조립          AI 본문 + 고정 푸터(중개사무소 정보/면책) 병합
        │                ★ 법정 명시사항은 AI가 아니라 코드가 렌더링
        ▼
[5] 중복·품질 게이트    이미 발행한 매물인가 / 최소 길이 / 금칙어 / 가격 일치
        │
        ▼
[6] Blogger 발행       POST /blogger/v3/blogs/{blogId}/posts  (isDraft=true 기본)
        │
        ▼
[7] 이력 저장          published-posts.json (매물번호 ↔ postId ↔ 발행시각)
```

**설계 원칙**: 3번에서 AI가 만드는 것은 *문장*뿐이다. 숫자(가격·면적·층)와 법정 명시사항은 2번과 4번에서
코드가 직접 넣는다. AI가 숫자를 다시 쓰게 하면 반드시 틀린다.

---

## 3. 사전 준비 (계정·콘솔 작업)

### 3.1 Blogger 블로그 만들기

1. https://www.blogger.com 에서 블로그 생성
2. 블로그 ID 확인 — 관리 페이지 URL의 `blogID=` 뒤 숫자, 또는 API로 조회:
   ```
   GET https://www.googleapis.com/blogger/v3/users/self/blogs
   ```

### 3.2 Google Cloud Console

1. 프로젝트 생성 → **Blogger API v3** 사용 설정
2. OAuth 동의 화면 구성
   - 사용자 유형: 외부
   - 게시 상태: **테스트** (본인만 쓸 경우) → 테스트 사용자에 본인 구글 계정 추가
3. 사용자 인증 정보 → OAuth 클라이언트 ID → 애플리케이션 유형 **데스크톱 앱**
4. 클라이언트 ID / 시크릿 확보

> **주의 — 배포 시 검증 필요**
> `https://www.googleapis.com/auth/blogger`는 구글이 **민감한 범위(sensitive scope)** 로 분류한다.
> 본인 계정만 쓰는 테스트 모드는 문제없지만, 이 EXE를 다른 중개사에게 배포해 각자 자기 블로그에 연동하게 하려면
> **OAuth 앱 검증(브랜드 확인 + 보안 심사)** 을 통과해야 한다. 미검증 상태로 배포하면 사용자에게
> "확인되지 않은 앱" 경고가 뜨고, 테스트 사용자 100명 제한에 걸린다.
> 배포 계획이 있으면 이 항목을 일정에 반드시 넣을 것.

### 3.3 OpenAI

1. https://platform.openai.com 에서 API 키 발급
2. 사용량 한도(Usage limits) 설정 — 자동화라 폭주하면 비용이 순식간에 늘어난다

---

## 4. 인증 설계 (Blogger OAuth 2.0)

WinForms 데스크톱 앱이므로 **Loopback + PKCE** 방식을 쓴다. (구글은 데스크톱 앱의 OOB 방식을 폐지했다.)

### 4.1 최초 연동 흐름

```
1) 앱이 임의 포트로 HttpListener 기동      http://127.0.0.1:{port}/
2) code_verifier 생성 → S256 해시로 code_challenge
3) 기본 브라우저로 동의 화면 열기
     https://accounts.google.com/o/oauth2/v2/auth
       ?client_id={CLIENT_ID}
       &redirect_uri=http://127.0.0.1:{port}/
       &response_type=code
       &scope=https://www.googleapis.com/auth/blogger
       &code_challenge={challenge}&code_challenge_method=S256
       &access_type=offline        ← refresh_token 발급에 필수
       &prompt=consent             ← 재동의 시에도 refresh_token 재발급
4) 사용자 동의 → 127.0.0.1로 ?code= 리다이렉트 → HttpListener가 수신
5) 토큰 교환  POST https://oauth2.googleapis.com/token
       code, client_id, client_secret, code_verifier,
       redirect_uri, grant_type=authorization_code
6) refresh_token을 DPAPI로 암호화해 settings.json에 저장
```

### 4.2 이후 발행 시

`access_token`은 1시간이면 만료되므로 `refresh_token`으로 재발급한다.

```
POST https://oauth2.googleapis.com/token
  client_id, client_secret, refresh_token, grant_type=refresh_token
```

### 4.3 토큰 저장

기존 프로젝트 관례를 그대로 따른다 — `DataProtection.Protect()`(Windows DPAPI)로 암호화해
`AppSettings`에 넣고 `LocalStore`가 저장한다.
경로: `%LocalAppData%\NaverPropertyRanking_Blog\settings.json`

```csharp
// AppSettings.cs 에 추가
public string EncryptedBloggerRefreshToken { get; set; } = string.Empty;
public string BloggerBlogId { get; set; } = string.Empty;
public string BloggerBlogName { get; set; } = string.Empty;

[System.Text.Json.Serialization.JsonIgnore]
public string BloggerRefreshToken { get; set; } = string.Empty;
```

`LocalStore.LoadSettings()` / `SaveSettings()`에 기존 `EncryptedBearerToken` 처리와 같은 방식으로
한 줄씩 추가하면 된다.

> **client_secret에 대해**: 데스크톱 앱의 클라이언트 시크릿은 EXE에서 추출 가능하므로 실질적 비밀이 아니다.
> 구글도 이를 전제로 하며, 실제 보호는 PKCE가 담당한다. 다만 평문으로 깃에 올라가면 곤란하니
> **`appsettings.example.json`에는 플레이스홀더만** 두고 실제 값은 gitignore된 `appsettings.json`에만 넣는다.
> (이 프로젝트는 이미 그 구조다.)

---

## 5. 본문 생성 (OpenAI)

### 5.1 모델 선택

2026-09 기준 [OpenAI 가격표](https://developers.openai.com/api/docs/pricing):

| 모델 | Input ($/1M) | Output ($/1M) | 용도 |
|---|---:|---:|---|
| `gpt-6-astra` | 10.00 | 50.00 | 플래그십. 이 용도엔 과하다 |
| `gpt-5.6-sol` | 4.00 | 20.00 | 복잡한 전문 업무 |
| `gpt-5.6-terra` | 2.00 | 12.00 | 지능/비용 균형 |
| **`gpt-5.6-luna`** | **0.20** | **1.20** | **비용 민감 워크로드 — 권장 기본값** |

**권장**: 기본 `gpt-5.6-luna`, 품질이 아쉬우면 `gpt-5.6-terra`로 승격. 설정에서 모델명을 바꿀 수 있게 한다.
블로그 글 한 편은 정형 데이터의 서술이라 최상위 모델이 필요한 작업이 아니다.

### 5.2 구조화된 출력

자유 텍스트로 받으면 파싱이 깨진다. **Structured Outputs**(JSON Schema)로 받는다.

```
POST https://api.openai.com/v1/responses
Authorization: Bearer {OPENAI_API_KEY}
Content-Type: application/json

{
  "model": "gpt-5.6-luna",
  "input": [
    { "role": "system", "content": "<시스템 프롬프트 — §5.4>" },
    { "role": "user",   "content": "<사실 카드 JSON>" }
  ],
  "text": {
    "format": {
      "type": "json_schema",
      "name": "blog_post",
      "strict": true,
      "schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["title", "summary", "labels", "sections"],
        "properties": {
          "title":   { "type": "string" },
          "summary": { "type": "string" },
          "labels":  { "type": "array", "items": { "type": "string" } },
          "sections": {
            "type": "array",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["heading", "bodyHtml"],
              "properties": {
                "heading":  { "type": "string" },
                "bodyHtml": { "type": "string" }
              }
            }
          }
        }
      }
    }
  }
}
```

`strict: true`면 스키마를 벗어난 응답이 오지 않으므로 파싱 실패 처리를 크게 줄일 수 있다.

### 5.3 이미지

**Blogger API v3에는 미디어 업로드 엔드포인트가 없다.** `Posts` 리소스의 `images` 필드는
본문에서 추출된 이미지를 알려주는 읽기 전용에 가깝다. 이미지를 쓰려면 **먼저 어딘가에 호스팅**하고
본문 HTML에 `<img src="...">`로 참조해야 한다.

| 방법 | 장점 | 단점 |
|---|---|---|
| **이미지 생략** (권장 — 1단계) | 구현 없음, 비용 0 | 글이 밋밋함 |
| 네이버 매물 사진 URL 직접 참조 | 무료, 실제 사진 | **핫링크 + 저작권 문제. 권장하지 않음** |
| 직접 찍은 사진을 GitHub/S3/R2에 업로드 | 안전, 진짜 매물 사진 | 업로드 파이프라인 필요 |
| `gpt-image-*` 생성 이미지 + 외부 호스팅 | 자동화 | 비용, 부동산엔 부적합 |

> 부동산 글에 **AI로 생성한 아파트 사진**을 넣는 것은 매물 오인 소지가 있어 피하는 게 좋다.
> 넣는다면 실사가 아닌 일러스트/인포그래픽 계열로 한정하고 AI 생성물임을 표기한다.

**1단계에서는 이미지 없이 텍스트만** 발행하고, 필요해지면 나중에 붙이는 것을 권한다.

### 5.4 프롬프트 설계

시스템 프롬프트 초안:

```
너는 한국 부동산 중개사무소의 블로그 글을 쓰는 작가다.

[입력]
사용자 메시지는 실제 매물의 검증된 사실만 담은 JSON이다.

[절대 규칙]
1. JSON에 없는 사실을 지어내지 마라. 특히 아래는 절대 창작 금지다.
   - 가격, 면적, 층수, 방/욕실 수, 준공년도, 관리비
   - 학군, 교통 개통 예정, 개발 호재, 시세 전망
2. JSON에 값이 없는 항목은 아예 언급하지 마라. "확인 필요"라고도 쓰지 마라.
3. 투자 수익률, 시세 상승 전망, 매수 권유를 쓰지 마라.
4. 숫자를 본문에 쓸 때는 JSON의 값을 글자 그대로 옮겨라. 반올림·환산 금지.
5. 최상급 표현(최고, 최저, 유일, 확실) 금지.

[문체]
- 친근한 존댓말 구어체. 이모지는 소제목당 최대 1개.
- 소제목 3~5개. 각 소제목 아래 2~4문단.
- 전체 900~1,400자.
- HTML로 작성하되 <h2> <h3> <p> <ul> <li> <table> <strong> 만 사용.
  <script> <style> <iframe> 및 인라인 style 속성 금지.

[출력]
지정된 JSON 스키마로만 응답한다.
```

사용자 메시지(사실 카드) 예:

```json
{
  "단지명": "아크로리버파크",
  "매물유형": "아파트",
  "거래유형": "매매",
  "가격": "42억",
  "소재지": "서울 서초구 반포동",
  "동": "101동",
  "층": "12/35층",
  "전용면적": "84.97㎡",
  "등록일": "2026-09-01",
  "특징": "한강 조망, 즉시 입주 가능"
}
```

### 5.5 비용 추정

글 1편 ≈ input 1,500토큰 + output 2,500토큰 가정:

| 모델 | 글 1편 | 하루 10편 | 한 달(30편/일) |
|---|---:|---:|---:|
| `gpt-5.6-luna` | 약 $0.0033 (≈4.6원) | $0.033 | 약 $3.0 (≈4,100원) |
| `gpt-5.6-terra` | 약 $0.033 (≈46원) | $0.33 | 약 $30 (≈41,000원) |

Batch API를 쓰면 50% 추가 절감이 가능하나 즉시 발행이 아니라 지연 처리가 된다.
대량등록 모드에만 선택적으로 적용할 만하다.

---

## 6. 발행 (Blogger API v3)

### 6.1 글 작성

```
POST https://www.googleapis.com/blogger/v3/blogs/{blogId}/posts?isDraft=true
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "kind": "blogger#post",
  "blog": { "id": "{blogId}" },
  "title": "반포동 아크로리버파크 84㎡ 매매 42억 매물 안내",
  "content": "<h2>...</h2><p>...</p>",
  "labels": ["서초구", "반포동", "아크로리버파크", "아파트매매"]
}
```

- 쓰기에는 **OAuth 2.0 토큰이 필수**다. API 키만으로는 불가능하다.
- `isDraft=true`로 초안 저장 후 사람이 확인하고 게시하는 흐름을 **기본값**으로 두기를 강력히 권한다 (§8).
- 초안을 나중에 게시: `POST /blogs/{blogId}/posts/{postId}/publish`

### 6.2 할당량 — 반드시 알고 있어야 할 함정

| 한도 | 값 | 비고 |
|---|---|---|
| API 요청 | 10,000회/일 | Cloud Console에서 확인 |
| **블로그당 글 생성** | **문서화되어 있지 않음** | 실사용 보고상 하루 수십~100건 근처에서 `403 Rate Limit Exceeded` |

두 번째 한도가 진짜 병목이다. [Blogger 커뮤니티 보고](https://support.google.com/blogger/thread/64728872?hl=en)에 따르면
API 요청 할당량이 남아 있어도 100건 근처에서 403이 난다. 스팸 호스팅 방지용 설계라 상향 신청 경로도 사실상 없다.

**대응**:
- 발행 간 간격을 두고(예: 5~10분) 큐로 처리
- `403` 수신 시 당일 발행 중단하고 다음 날 재개 — 기존 `RateLimitBlockedUntilUtc` 패턴을 그대로 재사용
- 하루 발행 상한을 설정값으로 두고 보수적으로(예: 10건) 시작

### 6.3 오류 처리

| 코드 | 의미 | 처리 |
|---|---|---|
| 401 | access_token 만료 | refresh_token으로 재발급 후 1회 재시도 |
| 403 `rateLimitExceeded` | 일일 한도 | 당일 중단, 쿨다운 기록 |
| 403 `insufficientPermissions` | 스코프 부족 | 재연동 유도 |
| 400 | 본문 오류 | HTML 정제 후 재시도, 실패 시 사람에게 |

---

## 7. 프로젝트 반영 계획

### 7.1 새로 만들 파일

```
src/NaverPropertyRanking_Blog/
├── Models/
│   └── BlogModels.cs                 // BlogPostFacts, GeneratedPost, BlogPublishResult,
│                                     // BlogConfiguration, OpenAiConfiguration
├── Services/
│   ├── BlogPostFactsBuilder.cs       // Listing → BlogPostFacts (숫자·법정항목 확정)
│   ├── OpenAiPostGenerator.cs        // Responses API 호출, Structured Outputs 파싱
│   ├── BlogHtmlComposer.cs           // AI 섹션 + 법정 푸터 → 최종 HTML, 태그 화이트리스트
│   ├── BlogPostGate.cs               // 중복/품질/금칙어 검사
│   ├── GoogleOAuthClient.cs          // Loopback + PKCE, 토큰 갱신
│   ├── BloggerPublisher.cs           // posts.insert / publish
│   └── PublishedPostStore.cs         // 발행 이력 (published-posts.json)
└── UI/
    ├── BlogChannelSettingsForm.cs    // 블로그 연동, 블로그 선택, 중개사무소 정보 입력
    └── BlogPostPreviewForm.cs        // 생성 결과 미리보기 → 수정 → 발행
```

### 7.2 의존성

**새 NuGet 패키지를 추가하지 않는 것을 권한다.** 이 프로젝트는 `System.Text.Encoding.CodePages` 하나만 참조하고
모든 HTTP 호출을 `HttpClient` + `System.Text.Json`으로 직접 처리하는 구조다.
`Google.Apis.Blogger.v3`나 OpenAI SDK를 넣으면 단일 파일 게시 크기와 트리밍 동작이 복잡해진다.
두 API 모두 REST 호출이 단순해서 직접 구현이 낫다.

### 7.3 설정 확장

`appsettings.json` / `appsettings.example.json`에 섹션 추가:

```json
"Blog": {
  "Enabled": false,
  "AutoPublish": false,
  "DailyPostLimit": 10,
  "MinIntervalMinutes": 10,
  "OpenAi": {
    "ApiKey": "",
    "Model": "gpt-5.6-luna",
    "Endpoint": "https://api.openai.com/v1/responses",
    "RequestTimeoutSeconds": 120
  },
  "Blogger": {
    "ClientId": "",
    "ClientSecret": "",
    "Scope": "https://www.googleapis.com/auth/blogger",
    "ApiBaseUrl": "https://www.googleapis.com/blogger/v3"
  },
  "Office": {
    "Name": "",
    "RegistrationNumber": "",
    "Address": "",
    "BrokerName": "",
    "Phone": ""
  }
}
```

`Models/ApplicationConfiguration.cs`의 `AppFileConfiguration`에
`public BlogConfiguration Blog { get; set; } = new();` 한 줄을 더하고 대응 클래스를 정의하면
기존 `ApplicationConfigurationLoader`가 그대로 읽는다.

> `OpenAi.ApiKey`와 `Blogger.ClientSecret`은 실제 `appsettings.json`에만 넣는다.
> `appsettings.example.json`에는 빈 문자열로 둔다.
> (`.gitignore`가 `src/NaverPropertyRanking_Blog/appsettings.json`을 이미 차단하고 있다.)

### 7.4 코드 스케치

```csharp
namespace NaverPropertyRanking_Blog.Models;

/// <summary>AI에 넘기기 전에 확정한 매물 사실. 여기 없는 값은 글에 나오면 안 된다.</summary>
public sealed record BlogPostFacts(
    string ArticleNo,
    string ComplexName,
    string RealEstateType,
    string TradeType,
    string Price,
    string Address,
    string Dong,
    string FloorInfo,
    string Area,
    string RegisteredDate,
    string Description);

/// <summary>OpenAI가 돌려준 글. 숫자는 검증 대상이다.</summary>
public sealed record GeneratedPost(
    string Title,
    string Summary,
    IReadOnlyList<string> Labels,
    IReadOnlyList<GeneratedSection> Sections);

public sealed record GeneratedSection(string Heading, string BodyHtml);
```

```csharp
namespace NaverPropertyRanking_Blog.Services;

/// <summary>
/// 매물 하나를 블로그 글로 만들어 Blogger에 올린다.
/// 숫자와 법정 명시사항은 AI를 거치지 않고 코드가 직접 넣는다.
/// </summary>
public sealed class BlogPostPipeline
{
    private readonly OpenAiPostGenerator _generator;
    private readonly BlogHtmlComposer _composer;
    private readonly BlogPostGate _gate;
    private readonly BloggerPublisher _publisher;
    private readonly PublishedPostStore _history;

    public async Task<BlogPublishResult> RunAsync(
        Listing listing,
        bool asDraft,
        CancellationToken cancellationToken)
    {
        if (_history.AlreadyPublished(listing.ArticleNo))
            return BlogPublishResult.Skipped("이미 발행한 매물입니다.");

        var facts = BlogPostFactsBuilder.Build(listing);
        var generated = await _generator.GenerateAsync(facts, cancellationToken);

        // AI가 가격·면적을 바꿔 썼으면 여기서 걸러진다.
        var gateError = _gate.Validate(facts, generated);
        if (gateError is not null) return BlogPublishResult.Rejected(gateError);

        var html = _composer.Compose(facts, generated);
        var result = await _publisher.InsertAsync(html, generated, asDraft, cancellationToken);
        if (result.Success) _history.Record(listing.ArticleNo, result.PostId, result.Url);
        return result;
    }
}
```

### 7.5 UI 연결

- `MainForm` 검색조건 줄에 **"블로그 발행"** 버튼 추가 (`_columnSettingsButton` 옆, 같은 크기 82×32)
- 그리드에서 체크된 매물 → 대량, 우클릭 → 단건
- 결과는 `BlogPostPreviewForm`에서 제목·본문 수정 후 발행
- 진행 상황은 기존 `BusyProgressOverlay` / `SetStatus` 재사용

### 7.6 스모크 테스트 추가

`tests/NaverPropertyRanking_Blog.SmokeTests/Program.cs` 관례(외부 호출 없이 `HttpMessageHandler` 스텁)를 따라:

| 테스트 | 검증 내용 |
|---|---|
| 사실 카드 생성 | `Listing` → `BlogPostFacts` 매핑, 빈 값 제외 |
| 구조화 응답 파싱 | 스텁 JSON → `GeneratedPost` |
| 숫자 불일치 차단 | 본문에 사실 카드와 다른 가격이 있으면 거부 |
| HTML 화이트리스트 | `<script>`·인라인 style 제거 |
| 법정 푸터 삽입 | 중개사무소 명칭/등록번호/소재지/성명이 최종 HTML에 존재 |
| 중복 발행 차단 | 같은 매물번호 두 번째 호출은 Skipped |
| 401 재시도 | 첫 호출 401 → 토큰 갱신 → 재시도 성공 |
| 403 쿨다운 | 일일 한도 도달 시 당일 중단 기록 |

---

## 8. 반드시 짚고 갈 리스크

이 기능은 기술보다 **규제·정책 리스크가 더 큰** 작업이다. 구현 전에 판단이 필요하다.

### 8.1 공인중개사법 — 인터넷 표시·광고 명시사항 (가장 중요)

중개대상물을 인터넷에 광고할 때 명시해야 할 항목이 법으로 정해져 있고,
**국토교통부 모니터링 대상에 블로그 게시물이 포함된다.**
자동 포스팅은 이 항목을 빠뜨린 글을 대량으로 만들어내는 일이므로 위험이 그만큼 증폭된다.

필수 명시사항:

- **중개사무소**: 명칭(등록증과 동일), 소재지, 등록번호, 개업공인중개사 성명
- **중개대상물(건축물)**: 소재지(지번·동·층수), 전용면적, 가격, 용도, 총 층수, 사용승인일,
  방/욕실 수, 입주가능일, 주차대수, 관리비(비목별 세부내역), 방향(기준점 명시)
- 확인 불가 항목은 **빈칸이 아니라 "확인 불가" 취지를 표기**
- 2025-01-01 시행 개정으로 **위반건축물 표기 의무화**

거래 완료 후 광고를 내리지 않으면 **500만원 이하 과태료**.

**설계 반영**:

1. 이 항목들은 AI가 아니라 `BlogHtmlComposer`가 **고정 템플릿으로 렌더링**한다.
2. 현재 `Listing` 레코드에는 **용도·사용승인일·방/욕실 수·주차대수·관리비·방향이 없다.**
   이 값들을 채우지 못하면 합법적인 광고 글을 만들 수 없다.
   → 상세 API(`ArticleDetail`)에서 추가 수집하거나 사용자가 입력하게 해야 한다. **선행 과제다.**
3. 매물이 거래 완료되면 해당 글을 자동으로 내리는(또는 초안 전환) 로직이 필요하다.
   기존 `ListingChangeDetector`가 매물 소멸을 감지하므로 여기에 연결할 수 있다.

### 8.2 구글 스팸 정책 — 대량 생성 콘텐츠 남용

구글은 AI 사용 자체를 금지하지 않지만,
**"사용자에게 가치를 더하지 않으면서 순위 조작 목적으로 많은 페이지를 생성하는 행위"** 를 제재한다.
적발되면 색인 제외, 애드센스를 붙였다면 계정 정지까지 이어진다.

**완화 방법**:

- 실제 보유 매물 1건 = 글 1건. 없는 매물이나 키워드용 글을 만들지 않는다 (우리 구조의 최대 강점)
- 하루 발행량을 낮게(10건 이하) 유지
- 템플릿 문장 반복을 피하도록 프롬프트에 문체 변주 지시
- **초안 발행 후 사람이 확인하는 흐름을 기본값으로** — `AutoPublish: false`

### 8.3 네이버 데이터 이용

이 앱은 네이버 부동산에서 매물·경쟁매물 데이터를 가져온다.
**내 매물 정보를 내 블로그에 쓰는 것**과 **타 중개사의 동일매물 정보를 재게시하는 것**은 성격이 전혀 다르다.
후자는 네이버 이용약관과 타사 영업정보 문제가 걸린다.

→ **블로그 글에는 `IsMine == true`인 매물만 사용하고, `Comparables`(동일매물)는 절대 넣지 않는다.**
경쟁 매물 가격 비교는 앱 내부 분석용으로만 쓴다.

### 8.4 비용 폭주

자동 스케줄이 잘못 돌면 OpenAI 비용이 계속 나간다.
OpenAI 콘솔의 usage limit과 앱의 `DailyPostLimit`을 **이중으로** 건다.

---

## 9. 단계별 로드맵

| 단계 | 범위 | 산출물 | 비고 |
|---|---|---|---|
| **0. 선행** | 법정 명시항목 데이터 확보 | `ArticleDetail` 확장 또는 수동 입력 UI | §8.1 — **이게 안 되면 나머지가 무의미** |
| **1. 골격** | 사실 카드 + OpenAI 생성 + 미리보기 | `BlogPostFactsBuilder`, `OpenAiPostGenerator`, `BlogPostPreviewForm` | 발행 없음. 글 품질만 확인 |
| **2. 연동** | OAuth + 단건 초안 발행 | `GoogleOAuthClient`, `BloggerPublisher`, `BlogChannelSettingsForm` | `isDraft=true` 고정 |
| **3. 운영** | 이력·중복방지·품질 게이트·한도 | `PublishedPostStore`, `BlogPostGate` | 403 쿨다운 포함 |
| **4. 자동화** | 대량 발행, 스케줄, 거래완료 시 내리기 | 큐 + 기존 폴링 루프 연결 | 여기서만 `AutoPublish` 허용 |
| **5. 선택** | 이미지 | 외부 호스팅 파이프라인 | §5.3 |

1단계까지만 해도 "글 초안을 AI가 써 주는 도구"로서 충분히 쓸모가 있다.
2단계부터는 §8의 판단이 끝난 뒤에 진행할 것을 권한다.

---

## 10. 참고 링크

**Blogger API**

- [Blogger API v3 사용 가이드](https://developers.google.com/blogger/docs/3.0/using)
- [posts.insert 레퍼런스](https://developers.google.com/blogger/docs/3.0/reference/posts/insert)
- [일일 한도 관련 커뮤니티 스레드](https://support.google.com/blogger/thread/64728872?hl=en)

**OpenAI**

- [모델 목록](https://developers.openai.com/api/docs/models)
- [가격](https://developers.openai.com/api/docs/pricing)

**정책·법령**

- [구글 검색 스팸 정책](https://developers.google.com/search/docs/essentials/spam-policies)
- [중개대상물의 표시·광고 명시사항 세부기준 (국가법령정보센터)](https://www.law.go.kr/LSW/admRulLsInfoP.do?admRulId=73758&efYd=0)
- [부동산 매물 광고 명시항목 체크리스트](https://wepick.kr/insight/property-listing-ad-disclosure-checklist/)
- [2025-01-01 시행 고시 개정 안내](https://www.karnews.or.kr/news/articleView.html?idxno=18012)

**벤치마크**

- [빠글 AI 소개](https://possible-element-35a.notion.site/AI-225471a8764980d4985bcd0ed9ff874b)
- [빠글 AI 공식 사이트](https://bbagle.ai/)
