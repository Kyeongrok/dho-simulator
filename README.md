# DirectXAppTemplate

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

`dotnet new` 명령 한 줄로 **C# DirectX 11 앱**을 바로 만드는 최소 템플릿입니다.

WPF·WinForms 없이 **순수 Win32 창 + D3D11 스왑체인**만으로 화면을 그립니다. D3D 바인딩은 [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)를 씁니다.

## 설치

```bash
dotnet new install DirectXAppTemplate
```

> 로컬 `.nupkg`로 설치하는 경우:
> ```bash
> dotnet pack DirectXAppTemplate.csproj -c Release -o nupkg
> dotnet new install ./nupkg/DirectXAppTemplate.1.0.0.nupkg
> ```

## 사용법

```bash
dotnet new dx-app -n MyGame
cd MyGame
dotnet run
```

`MyGame` 자리에 넣은 이름으로 프로젝트 이름·네임스페이스·파일명이 모두 바뀝니다.

## 생성되는 프로젝트 구조

```
MyGame/
├── MyGame.csproj          ← WinExe, Vortice.Direct3D11 / Vortice.D3DCompiler
├── app.manifest           ← PerMonitorV2 DPI 인식
├── Program.cs             ← [STAThread] Main
├── GameWindow.cs          ← 창 · 메시지 루프 · D3D11 · 그리기
└── Native/
    └── Win32.cs           ← 창을 만드는 데 필요한 최소한의 P/Invoke
```

## 그리는 방식

```
Tick(dt) ─▶ Compose()  CPU 에서 480x320 BGRA 버퍼(_fb)에 그림
         ─▶ Upload()   동적 텍스처에 Map/WriteDiscard 로 복사
         ─▶ Draw()     정점 버퍼 없이 화면 가득 삼각형 1장 → 점 샘플링 2배 확대 → Present
```

- 해상도/배율은 `GameWindow`의 `BoardWidth`, `BoardHeight`, `Zoom` 상수로 바꿉니다.
- 게임 로직은 `Tick(dt)`, 그림은 `Compose()`에 넣으면 됩니다. 처음엔 사각형 하나가 튕겨 다닙니다.
- 키 입력은 `WndProc`의 `WM_KEYDOWN`에서 받습니다. `ESC`는 종료입니다.
- 셰이더는 `CreateDevice()` 안의 HLSL 문자열을 런타임에 `D3DCompiler`로 컴파일합니다.

## 요구사항

- .NET 8.0 이상
- Windows (DirectX 11 지원 GPU)

## 템플릿 제거

```bash
dotnet new uninstall DirectXAppTemplate
```

## 라이선스

MIT License
