# Atomos

[![GitHub release (latest by date)](https://img.shields.io/github/v/release/CouncilOfTsukuyomi/Atomos)](https://github.com/CouncilOfTsukuyomi/Atomos/releases)
[![GitHub all releases](https://img.shields.io/github/downloads/CouncilOfTsukuyomi/Atomos/total)](https://github.com/CouncilOfTsukuyomi/Atomos/releases)
[![GitHub release (latest by date)](https://img.shields.io/github/downloads/CouncilOfTsukuyomi/Atomos/latest/total)](https://github.com/CouncilOfTsukuyomi/Atomos/releases/latest)
[![Build Status](https://img.shields.io/github/actions/workflow/status/CouncilOfTsukuyomi/Atomos/release.yml?branch=main)](https://github.com/CouncilOfTsukuyomi/Atomos/actions)

[![GitHub issues](https://img.shields.io/github/issues/CouncilOfTsukuyomi/Atomos)](https://github.com/CouncilOfTsukuyomi/Atomos/issues)
[![GitHub pull requests](https://img.shields.io/github/issues-pr/CouncilOfTsukuyomi/Atomos)](https://github.com/CouncilOfTsukuyomi/Atomos/pulls)
[![GitHub stars](https://img.shields.io/github/stars/CouncilOfTsukuyomi/Atomos?style=social)](https://github.com/CouncilOfTsukuyomi/Atomos/stargazers)
[![License](https://img.shields.io/github/license/CouncilOfTsukuyomi/Atomos)](https://github.com/CouncilOfTsukuyomi/Atomos/blob/main/LICENSE)

Atomos is a tool for managing and installing mods for Final Fantasy XIV through integration with Penumbra.

## Getting Started

### Installation

Download the latest release from the GitHub repository and run `Launcher.exe`. A console window will appear first, followed by the main UI.

### Support

If you encounter any issues, please report them in the **Penumbra Mod Forwarder** category on [our Discord server](https://discord.gg/rtGXwMn7pX).

## Features

- **Mod Tracking**: Keeps track of installed mods
- **Error Reporting**: Optional Sentry integration for automatic error reporting
- **Penumbra Integration**: Seamless integration with the Penumbra mod framework

## Known Issues

- **TexTools Conversion**: Occasional TexTools conversion failures may occur (program continues to function)

## Error Reporting (Sentry)

When Sentry logging is enabled, the following information is automatically sent when errors occur:
- Error message and stack trace
- Windows version
- .NET runtime version

This helps us identify and fix issues quickly. Error reporting is optional and can be disabled in settings.

## Upcoming Features


## Plugins

Atomos now features a plugin system that allows users and developers to create custom plugins for different mod websites and services.

### Creating Your Own Plugin

A GitHub template is available to help you get started with plugin development:
**[Plugin Template Repository](https://github.com/CouncilOfTsukuyomi/PluginTemplate)**

This template provides:
- Basic plugin structure and interfaces
- Configuration schema examples
- Build and packaging scripts

## XIV Mod Archive (XMA) Plugin

The XMA Plugin enables browsing and downloading mods from XIV Mod Archive,
including NSFW content with proper authentication.

**Source Code:** [XMA Plugin Repository](https://github.com/CouncilOfTsukuyomi/XMA-Plugin)

### Setup Instructions

To access NSFW mods on XIV Mod Archive, you'll need to provide your authentication cookie.

#### Security Note
**Your cookie is only used for authenticated requests to XMA and is stored locally on your machine.**

#### Step-by-Step Setup

1. **Install a cookie editor extension** for your browser:
    - Recommended: [Cookie Editor](https://cookie-editor.com/)
    - Available for Chrome, Firefox, and other browsers

2. **Log into XIV Mod Archive**
    - Go to [XIV Mod Archive](https://www.xivmodarchive.com)
    - Sign in with your account

3. **Extract your authentication cookie**
    - Open the cookie editor extension
    - Find the cookie named `connect.sid`
    - Copy its value (this is your authentication token)

4. **Configure the plugin in Atomos**
    - Open Atomos and go to the Plugins section
    - Find the XMA Plugin and click Settings
    - Paste the cookie value into the appropriate field
    - Save your settings

5. **Verify setup**
    - The plugin should now be able to access NSFW content and your account features
    - Try browsing mods to confirm everything is working

#### Troubleshooting

- **Cookie not working?** Make sure you copied the entire `connect.sid` value
- **Still can't see NSFW mods?** Verify your XMA account has NSFW content enabled
- **Plugin not loading?** Check that the plugin is enabled in the Plugins section

#### Privacy & Security

- Your authentication cookie is stored locally and never transmitted to any servers other than XIV Mod Archive
- You can remove the cookie at any time by clearing the plugin settings
- The cookie will expire, according to XIV Mod Archive's session policies

## Contributing

Please report bugs and feature requests through our Discord server in the appropriate channels. Pull requests are welcome on the GitHub repository.