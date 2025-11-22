#!/usr/bin/env python3

import os
import sys
import xml.etree.ElementTree as ET
import subprocess

def fail(msg: str):
    print(f"Error: {msg}")
    sys.exit(1)

def get_latest_git_tag():
    """Get the latest git tag to understand what version was last released"""
    try:
        latest_tag = subprocess.check_output(
            ["git", "describe", "--tags", "--abbrev=0"],
            stderr=subprocess.DEVNULL
        ).decode().strip()
        # Remove 'v' prefix if present and any beta suffix
        clean_tag = latest_tag.replace('v', '').replace('-b', '')
        return clean_tag
    except subprocess.CalledProcessError:
        return "0.0.0"  # No tags exist

def main():
    # 1) Check if Directory.Build.props exists
    if not os.path.isfile("Directory.Build.props"):
        fail("Directory.Build.props not found.")

    # 2) Parse the XML
    try:
        tree = ET.parse("Directory.Build.props")
    except Exception as ex:
        fail(f"Could not parse XML from Directory.Build.props: {ex}")

    root = tree.getroot()
    property_group = root.find("PropertyGroup")
    if property_group is None:
        fail("No <PropertyGroup> found in Directory.Build.props.")

    # Helper to find a text value or fail
    def get_node_text(tag_name):
        node = property_group.find(tag_name)
        if node is None or not node.text:
            fail(f"<{tag_name}> not found in Directory.Build.props.")
        return node.text.strip()

    major_str = get_node_text("MajorVersion")
    minor_str = get_node_text("MinorVersion")
    patch_str = get_node_text("PatchVersion")

    # 3) Convert to integers
    try:
        current_major = int(major_str)
        current_minor = int(minor_str)
        current_patch = int(patch_str)
    except ValueError:
        fail("Major/Minor/Patch elements must be valid integers.")

    # 4) Get release type and beta flag
    release_type = os.environ.get("RELEASE_TYPE", "patch")
    is_beta = os.environ.get("IS_BETA", "false").lower() == "true"

    # 5) Get the latest released version from git tags
    latest_released = get_latest_git_tag()
    latest_parts = latest_released.split('.')

    try:
        latest_major = int(latest_parts[0]) if len(latest_parts) > 0 else 0
        latest_minor = int(latest_parts[1]) if len(latest_parts) > 1 else 0
        latest_patch = int(latest_parts[2]) if len(latest_parts) > 2 else 0
    except ValueError:
        latest_major = latest_minor = latest_patch = 0

    # 6) Calculate what the next version should be based on release type
    if release_type == "patch":
        target_major = latest_major
        target_minor = latest_minor
        target_patch = latest_patch + 1
    elif release_type == "minor":
        target_major = latest_major
        target_minor = latest_minor + 1
        target_patch = 0
    elif release_type == "major":
        target_major = latest_major + 1
        target_minor = 0
        target_patch = 0
    else:
        fail(f"Unknown release type: {release_type}")

    # 7) Check if current version is already at target version
    current_version = f"{current_major}.{current_minor}.{current_patch}"
    target_version = f"{target_major}.{target_minor}.{target_patch}"

    if current_version == target_version:
        # We're already at the target version, don't increment
        final_major = current_major
        final_minor = current_minor
        final_patch = current_patch
        print(f"Current version {current_version} is already at target for {release_type} release")
    else:
        # Set to target version
        final_major = target_major
        final_minor = target_minor
        final_patch = target_patch
        print(f"Incrementing from {current_version} to {target_version}")

    # 8) Build version string
    new_version = f"{final_major}.{final_minor}.{final_patch}"
    if is_beta:
        new_version += "-b"

    # 9) Print outputs for subsequent steps
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a") as f:
            f.write(f"fullsemver={new_version}\n")
            f.write(f"major={final_major}\n")
            f.write(f"minor={final_minor}\n")
            f.write(f"patch={final_patch}\n")
    else:
        print(f"fullsemver={new_version}")
        print(f"major={final_major}")
        print(f"minor={final_minor}")
        print(f"patch={final_patch}")

    # 10) For tag comparison, get the previous tag
    try:
        previous_tag = subprocess.check_output(
            ["git", "describe", "--tags", "--abbrev=0"],
            stderr=subprocess.DEVNULL
        ).decode().strip()
    except subprocess.CalledProcessError:
        previous_tag = ""

    if github_output:
        with open(github_output, "a") as f:
            f.write(f"previous_tag={previous_tag}\n")
    else:
        print(f"previous_tag={previous_tag}")

if __name__ == "__main__":
    main()