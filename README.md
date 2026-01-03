<div align="center">
  <img width="50" height="50" alt="cssharp" src="https://github.com/user-attachments/assets/3393573f-29be-46e1-bc30-fafaec573456" />
	<h3><strong>Prop Hunt</strong></h3>
	<h4>a classic for fun gamemode</h4>
	<h2>
		<img src="https://img.shields.io/github/downloads/exkludera-cssharp/PropHunt/total" alt="Downloads">
		<img src="https://img.shields.io/github/stars/exkludera-cssharp/PropHunt?style=flat&logo=github" alt="Stars">
		<img src="https://img.shields.io/github/forks/exkludera-cssharp/PropHunt?style=flat&logo=github" alt="Forks">
		<img src="https://img.shields.io/github/license/exkludera-cssharp/PropHunt" alt="License">
	</h2>
	<!--<a href="https://discord.gg" target="_blank"><img src="https://img.shields.io/badge/Discord%20Server-7289da?style=for-the-badge&logo=discord&logoColor=white" /></a> <br>-->
	<a href="https://ko-fi.com/exkludera" target="_blank"><img src="https://img.shields.io/badge/KoFi-af00bf?style=for-the-badge&logo=kofi&logoColor=white" alt="Buy Me a Coffee at ko-fi.com" /></a>
	<a href="https://paypal.com/donate/?hosted_button_id=6AWPNVF5TLUC8" target="_blank"><img src="https://img.shields.io/badge/PayPal-0095ff?style=for-the-badge&logo=paypal&logoColor=white" alt="PayPal"  /></a>
	<a href="https://github.com/sponsors/exkludera" target="_blank"><img src="https://img.shields.io/badge/Sponsor-696969?style=for-the-badge&logo=github&logoColor=white" alt="GitHub Sponsor" /></a>
</div>

> [!NOTE]
> decided to create a new plugin instead of updating my fork of [Siomek101/CS2PropHunt](https://github.com/Siomek101/CS2PropHunt)
>
> <img src="https://github.com/user-attachments/assets/53e486cc-8da4-45ab-bc6e-eb38145aba36" height="200px"> <br>


## Requirements

- [MetaMod](https://github.com/alliedmodders/metamod-source)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
- [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager)
- [CS2MenuManager](https://github.com/schwarper/CS2MenuManager)

## Config

<details>
<summary>PropHunt.json</summary>
  
```json
{
  "Settings": {
    "Prefix": "{lightblue}[PropHunt]",
    "MenuType": "CenterHtmlMenu",
    "TeamScramble": true,
    "Hiding": {
      "Team": "T",
      "Time": 60,
      "DecoyLimit": 2,
      "SwapLimit": 10,
      "TauntLimit": 10
    }
  },
  "Sounds": {
    "SoundEvents": "soundevents/example.vsndevts",
    "RoundStart": [
      "balkan.radio_letsgo01",
      "balkan.radio_letsgo02",
      "balkan.radio_letsgo06",
      "balkan.radio_letsgo07",
      "balkan.radio_letsgo010"
    ],
    "LastAlive": [
      "balkan.lastmanstanding06"
    ],
    "Taunt": [
      "training.commander_comment_19",
      "training.commander_comment_21",
      "training.commander_comment_22"
    ]
  }
}
```
</details>