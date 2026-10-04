# dho — 대항해시대 온라인 항해 (개인용 싱글)

설치된 클라이언트(`C:\Netmarble\GV Online Kr`, 환경 변수 `GVO_DIR` 로 바꿈)의 자료 파일을 그 자리에서 읽어
항구와 바다를 그리고, 모험 의뢰 하나를 받아 끝낼 수 있게 한 것. 게임 자료는 저장소에 넣지 않는다.

```powershell
dotnet build dho.slnx
dotnet run --project src\Dho          # 게임
dotnet run --project src\Dho.Tools    # 개발도구 — data\*.json 을 고친다
```

| 프로젝트 | 내용 |
|---|---|
| `Dho.Data` | 게임 파일 읽개, 게임이 쓰는 자료(JSON) |
| `Dho` | 게임 — Win32 창, D3D11, Direct2D |
| `Dho.Tools` | 개발도구(WPF) — 의뢰 짓기, 지도에서 상륙지 찍기, 표·설정 고치기 |

- `data\settings.json` · `quests.json` · `landing-points.json` — 지은 자료
- `data\extracted\` — 클라이언트에서 뽑은 표(없으면 처음 실행할 때 만든다, 저장소에 두지 않는다)
- `tools\gvo\` — 분석에 쓴 파이썬

조작: 바다에서 `W`/`S` 돛, `A`/`D` 키, 바다 클릭으로 방향, `F` 입항·상륙. 마우스 오른쪽 끌기로 시점, 휠로 거리.

분석 글과 매뉴얼은 Obsidian 볼트 `mv` 의 `Project\dho` 에 있다.
