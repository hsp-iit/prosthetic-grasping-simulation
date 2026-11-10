namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Stops the data collection, both in the Editor and in a standalone
    /// player (where UnityEditor.EditorApplication is not available)
    /// </summary>
    public static class SimulationStopper
    {
        public static void Stop()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(1);
#endif
        }
    }
}
