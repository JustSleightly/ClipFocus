# ClipFocus [<img src="https://github.com/JustSleightly/Resources/raw/main/Icons/JSLogo.png" width="30" height="30">](https://vrc.sleightly.dev/ "JustSleightly") [<img src="https://github.com/JustSleightly/Resources/raw/main/Icons/Discord.png" width="30" height="30">](https://discord.sleightly.dev/ "Discord") [<img src="https://github.com/JustSleightly/Resources/raw/main/Icons/GitHub.png" width="30" height="30">](https://github.sleightly.dev/ "Github") [<img src="https://github.com/JustSleightly/Resources/raw/main/Icons/Store.png" width="30" height="30">](https://store.sleightly.dev/ "Store")

[![GitHub stars](https://img.shields.io/github/stars/JustSleightly/ClipFocus)](https://github.com/JustSleightly/ClipFocus/stargazers) [![GitHub Tags](https://img.shields.io/github/tag/JustSleightly/ClipFocus)](https://github.com/JustSleightly/ClipFocus/tags) [![GitHub release (latest by date including pre-releases)](https://img.shields.io/github/v/release/JustSleightly/ClipFocus?include_prereleases)](https://github.com/JustSleightly/ClipFocus/releases) [![GitHub issues](https://img.shields.io/github/issues/JustSleightly/ClipFocus)](https://github.com/JustSleightly/ClipFocus/issues) [![GitHub last commit](https://img.shields.io/github/last-commit/JustSleightly/ClipFocus)](https://github.com/JustSleightly/ClipFocus/commits/main) [![Discord](https://img.shields.io/discord/780192344800362506)](https://discord.sleightly.dev/)

![CF Script Showcase gif](https://github.com/JustSleightly/ClipFocus/raw/main/Documentation~/Gifs/CF%20Script%20Showcase.gif)

**Instant Animation window focusing for Unity's Animator workflow**

Stop clicking through Animation window dropdowns. Click states and BlendTree clips directly in the Animator window - ClipFocus handles the rest.

### Features

- **One-Click Focusing**: Click Animator States or BlendTree clips → Animation windows instantly focus
- **Multi-Window Support**: Focuses ALL unlocked Animation windows simultaneously
- **Full BlendTree Support**: Child clips maintain recordable state
- **Smart Lock Handling**: Respects locked windows, processes newly unlocked ones
- **Zero Setup**: Works immediately after installation

### Download the **[latest version](https://github.com/JustSleightly/ClipFocus/releases)** for free
### Add to **[VRChat Creator Companion](https://vpm.sleightly.dev/)**

## Quick Start

1. **Select a GameObject** with an Animator component in the Hierarchy
2. **Open an Animation window** (`Window > Animation > Animation` or `Right-Click another tab > Add Tab > Animation`)
3. **Click any Animator State or BlendTree clip** in the Animator window
4. **Animation window instantly focuses** on that clip - ready to record!

That's it. No configuration needed.

## Technical Details

### Requirements
- Tested on Unity 2022.3.22f1 intended for use with [VRChat](https://creators.vrchat.com/sdk/upgrade/current-unity-version)
  - Does not depend on the VRChat SDK, therefore compatible with Unity standalone

### Debug Logging
Enable detailed logging for troubleshooting:
- Menu: `JustSleightly > ClipFocus > Enable Debug Logs`
- Logs show clip processing, window state changes, and validation messages

## Known Issues
- When clicking on a BlendTree animation clip, unlocked animation windows will focus on the animation clip "greyed-out" for a frame before visibly flickering into a recordable status.
- After clicking on a BlendTree animation clip, unlocking a locked animation window will re-focus onto the selected BlendTree animation clip rather than maintaining the original locked clip like native Unity behaviour

## Frequently Asked Questions

<details>

  <summary> <strong> What happens if the animation window is greyed out and won't focus on my clip? </strong> </summary>
​
<blockquote>

Whenever you have difficulty with ClipFocus, try clicking off to another gameobject in the hierarchy and back to the original in order to re-initialize any unlocked animation windows. If having further difficulty, please feel free to reach out on [discord](https://discord.sleightly.dev).

</details>

## Acknowledgments

Inspired by [Dreadrith's](https://www.dreadrith.com/) version from Controller Editor before its end of service.

New version originally started by [Pancake992](https://pancake992.gumroad.com/) requested by [Lod](https://lodsgalaxy.com/)

Improved and maintained by [JustSleightly](https://github.sleightly.dev)