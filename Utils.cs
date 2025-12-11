namespace VideoStuff {
    public static class Utils {
        extension(float val) {
            public int Round() => (int)Math.Round(val);

            public int Ceiling() => (int)Math.Ceiling(val);
            public int Floor() => (int)Math.Floor(val);
        }

        extension(double val) {
            public int Round() => (int)Math.Round(val);

            public int Ceiling() => (int)Math.Ceiling(val);
            public int Floor() => (int)Math.Floor(val);
        }
    }
}
