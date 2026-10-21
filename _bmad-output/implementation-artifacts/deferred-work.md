- source_spec: `_bmad-output/implementation-artifacts/spec-1-1-שלד-מתפקד.md`
  summary: להוסיף בדיקה שמוכיחה ש-OriginSecretMiddleware רץ לפני UseForwardedHeaders, ובדיקה ל-middleware.js של Vercel.
  evidence: הסדר והיעד נשברים בלי שאף בדיקה תיכשל; אין כרגע נקודת קצה מעבר ל-/healthz שתושפע.
- source_spec: `_bmad-output/implementation-artifacts/spec-1-1-שלד-מתפקד.md`
  summary: להוסיף נקודת בריאות מינימלית בלי סוד (liveness) כדי ש-Render יפעיל healthCheckPath, ולהוסיף CI שמריץ dotnet test ו-npm build.
  evidence: /healthz דורש x-origin-secret ולכן render.yaml בלי healthCheckPath; בלי CI אין ריצה אוטומטית של הבדיקות.
