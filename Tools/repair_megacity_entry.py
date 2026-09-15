"""Declare a small source adaptation of the pinned official Megacity application.

No input injection, reflection, alternate scene loader, or generated content.
The original UI and an opt-in host adapter share one ordinary application API.
Run under Invoke-PsoNativeStage, before importing/building the changed host.
"""
from __future__ import annotations

import argparse
import difflib
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path

PIN = "07652ee74a1f322c2c3e607020f07be720175680"
FILES = {
    "Assets/Scripts/Gameplay/Client/UI/MonoBehaviours/MainMenu/MainMenu.cs":
        "b11984cbb5d19249874f966f586108c0fccd4e2052663db4469e258a9fb4300b",
    "Assets/Scripts/Gameplay/Client/UI/MonoBehaviours/Misc/TutorialScreen.cs":
        "eba02539b9d84b47854962a8878bcfefd58d51f13a2e9b16c24bc047632c48d1",
    "Assets/Scripts/Gameplay/Mix/Player/SpawnSinglePlayer.cs":
        "f1c15e786e9a18bb5391cfe6ac3ffaef381c14182363d2b4047110e5e19d5502",
}


def replace_once(text: str, old: str, new: str) -> str:
    if text.count(old) != 1:
        raise ValueError(f"Expected exactly one original source fragment: {old[:90]!r}")
    return text.replace(old, new, 1)


def adapt(path: str, text: str) -> str:
    if path.endswith("/MainMenu.cs"):
        text = replace_once(text, "        public static MainMenu Instance { get; private set; }",
            """        public static MainMenu Instance { get; private set; }

        // Explicit application readiness, not the enum's default MainMenu value.
        public bool IsInitialized { get; private set; }""")
        text = replace_once(text, "            CurrentState = MenuState.MainMenu;\n#endif",
            "            CurrentState = MenuState.MainMenu;\n            IsInitialized = true;\n#endif")
        text = replace_once(text, """        private void OnSinglePlayerButtonClicked()
        {
            m_PlayerSettings.GameMode = GameMode.SinglePlayer;
            SceneController.LoadGame();
            Hide();
        }""", """        private void OnSinglePlayerButtonClicked()
        {
            TryBeginSinglePlayer();
        }

        // The existing UI and opt-in acceptance entry share the original action.
        // Original GameMode, asynchronous Main loading and LoadingScreen remain.
        public bool TryBeginSinglePlayer()
        {
            if (!IsInitialized || CurrentState != MenuState.MainMenu ||
                m_PlayerSettings == null || m_MainMenuContainer == null ||
                LoadingScreen.Instance == null)
                return false;
            m_PlayerSettings.GameMode = GameMode.SinglePlayer;
            SceneController.LoadGame();
            Hide();
            return true;
        }""")
        return text
    if path.endswith("/TutorialScreen.cs"):
        text = replace_once(text, "using System.Collections;", "using IDisposable = System.IDisposable;\nusing System.Collections;")
        text = replace_once(text, "        private bool m_InTutorialScreen;", """        private bool m_InTutorialScreen;
        private IDisposable m_AnyInputSubscription;
        public bool IsShowingTutorial => m_InTutorialScreen;""")
        text = replace_once(text, '            m_SpeedSlider = root.Q<VisualElement>("speed-slider");',
            '''            m_SpeedSlider = root.Q<VisualElement>("speed-slider");
            if (m_InTutorialScreen)
            {
                m_TutorialScreen.style.display = DisplayStyle.Flex;
                SubscribeToDismissInput();
            }''')
        text = replace_once(text, "            StartCoroutine(WaitForAnyInput());", "            SubscribeToDismissInput();")
        text = replace_once(text, """        private void HideTutorial()
        {
            m_TutorialScreen.style.display = DisplayStyle.None;
            m_InTutorialScreen = false;
        }

        private IEnumerator WaitForAnyInput()
        {
            while (m_InTutorialScreen)
            {
                InputSystem.onAnyButtonPress.CallOnce(_ => { HideTutorial(); });
                yield return null;
            }
        }""", """        // Ordinary action shared by real input and the declared opt-in adapter.
        // This dismisses tutorial instructions only, never the loading screen.
        public void DismissTutorial()
        {
            ReleaseDismissInput();
            m_TutorialScreen.style.display = DisplayStyle.None;
            m_InTutorialScreen = false;
        }

        private void SubscribeToDismissInput()
        {
            ReleaseDismissInput();
            m_AnyInputSubscription = InputSystem.onAnyButtonPress.CallOnce(_ => DismissTutorial());
        }

        private void ReleaseDismissInput()
        {
            m_AnyInputSubscription?.Dispose();
            m_AnyInputSubscription = null;
        }

        private void OnDisable() => ReleaseDismissInput();

        private void OnDestroy()
        {
            ReleaseDismissInput();
            if (Instance == this) Instance = null;
        }""")
        return text
    return replace_once(text, "            var cmdBuffer = new EntityCommandBuffer(Allocator.Temp);",
        "            using var cmdBuffer = new EntityCommandBuffer(Allocator.Temp);")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    host, output = args.host.resolve(), args.output.resolve()
    repo = Path(__file__).resolve().parents[1]
    if not host.is_relative_to(repo / "work") or output.exists():
        raise ValueError("Use the retained host under repository work and a fresh receipt directory.")
    head = subprocess.check_output(["git", "-C", str(host), "rev-parse", "HEAD"], text=True).strip()
    if head != PIN:
        raise ValueError(f"Unexpected official source pin: {head}")
    prepared = []
    for relative, expected in FILES.items():
        original = (host / relative).read_bytes()
        if hashlib.sha256(original).hexdigest() != expected:
            raise ValueError(f"Original working source preimage differs: {relative}")
        git_original = subprocess.check_output(["git", "-C", str(host), "show", f"{PIN}:{relative}"])
        if original.replace(b"\r\n", b"\n") != git_original.replace(b"\r\n", b"\n"):
            raise ValueError(f"Working preimage differs from pinned Git source: {relative}")
        before = original.decode("utf-8").replace("\r\n", "\n")
        after = adapt(relative, before)
        newline = "\r\n" if b"\r\n" in original else "\n"
        prepared.append((relative, original, after.replace("\n", newline).encode("utf-8"), before, after))
    output.mkdir(parents=True)
    # All pin/fragment checks above precede the first mutation. Preserve exact
    # preimages even if a later filesystem write or receipt operation fails.
    for relative, original, _, _, _ in prepared:
        saved = output / "original" / relative
        saved.parent.mkdir(parents=True, exist_ok=True)
        saved.write_bytes(original)
    receipt, patch = [], []
    for relative, original, changed, before, after in prepared:
        (host / relative).write_bytes(changed)
        saved = output / "adapted" / relative
        saved.parent.mkdir(parents=True, exist_ok=True)
        saved.write_bytes(changed)
        receipt.append({"path": relative, "originalSha256": hashlib.sha256(original).hexdigest(),
                        "adaptedSha256": hashlib.sha256(changed).hexdigest()})
        patch.extend(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
                                         fromfile="original/" + relative, tofile="adapted/" + relative))
    (output / "declared-source.diff").write_text("".join(patch), encoding="utf-8", newline="\n")
    (output / "receipt.json").write_text(json.dumps({
        "schemaVersion": 1, "utc": datetime.now(timezone.utc).isoformat(), "sourceCommit": PIN,
        "host": str(host), "files": receipt, "inputSimulation": False, "reflection": False,
        "entry": "Opt-in application API after initialized original Menu; same SinglePlayer mode, async Main and loading screen.",
        "contentChanges": "None; all original scenes, materials, camera and simulation remain.",
        "ownershipRepairs": ["One owned tutorial input subscription per display, disposed on dismissal/disable/destroy.",
                             "Dispose the original temporary spawn EntityCommandBuffer after Playback."],
        "runtimeValidated": False,
    }, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"adaptedFiles": len(receipt), "receipt": str(output / "receipt.json")}))


if __name__ == "__main__":
    main()
