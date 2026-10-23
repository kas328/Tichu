# 스토어·런처 아이콘 원본

그림은 이 폴더의 HTML(인라인 SVG, 외부 리소스 없음)이 원본이다. 고칠 땐 HTML 을 고치고 다시 렌더한다.
용·봉황 그림은 `Assets/_Project/Art/Cards/Icons/Dragon.svg`·`Phoenix.svg` 를 그대로 옮긴 것, 색은 게임 코드와 같다(펠트 `#123821`, 카드 `#f7fafc`, 금 테두리 `#d9a838`).

저장소 루트에서:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/store/src/render.ps1              # HTML → PNG (Edge 헤드리스)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/store/src/check-store-images.ps1  # 규격 검사
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/store/src/check-listing.ps1       # 등록정보 글자 수 검사
```

`.ps1` 은 UTF-8 **BOM** 으로 저장한다(Windows PowerShell 5.1 이 BOM 없으면 한글을 깨뜨린다).

| 산출물 | 크기 | 알파 | 원본 |
|---|---|---|---|
| `docs/store/icon-512.png` | 512×512 | 상관없음 | `icon.html` |
| `docs/store/feature-1024x500.png` | 1024×500 | **없음**(렌더 후 24비트로 다시 저장) | `feature.html` |
| `docs/store/screens/*.png` | 1920×1080 | **없음** | Unity Play 모드 캡처 |
| `Assets/_Project/Art/AppIcon/icon-bg.png` | 432×432 | 상관없음 | `appicon-bg.html` |
| `Assets/_Project/Art/AppIcon/icon-fg.png` | 432×432 | **있음**, 그림은 중심 지름 264px 원 안(적응형 안전 영역 66dp) | `appicon-fg.html` |
| `Assets/_Project/Art/AppIcon/icon-legacy.png` | 432×432 | 상관없음(Round·Legacy 공용) | `appicon-legacy.html` |
