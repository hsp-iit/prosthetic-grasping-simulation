# Local patches

This is `com.unity.perception@1.0.0-preview.1` from the Unity registry, embedded
so it can be patched to compile on Unity 6000.6 (the upstream repository is archived).

Changes from the registry version:

- `Runtime/Randomization/Randomizers/RandomizerExamples/Utilities/GameObjectOneWayCache.cs`:
  `Object.GetInstanceID()` is a compile error in Unity 6000.6, replaced by `GetEntityId()`
  (the cache dictionary is now keyed by `EntityId`).
- `Editor/Randomization/VisualElements/Basic/UIntField.cs`: removed the `UxmlFactory` /
  `UxmlTraits` classes, since that UI Toolkit API was removed in Unity 6000.6. The field is
  only created from C# (`UIntDrawer`), never from UXML.
- Removed `Editor/Randomization/VisualElements/Basic/UxmlUIntAttributeDescription.cs` (only
  used by the traits above).
- Removed `Samples~`, `Tests` and `Documentation~` to keep the repository small, and the
  `samples` entry from `package.json`.
