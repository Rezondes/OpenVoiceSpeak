# So funktioniert der Ton

Diese Seite beschreibt den Weg der Sprache vom Mikrofon bis zu den Lautsprechern der anderen und wie die Hinweistöne dazukommen. Die Zahlen stammen aus dem Code; wo eine Stelle genauer nachzulesen ist, steht die Datei dabei. Die Entscheidungen und ihre Gründe stehen in `docs/archive/PLAN-ARCHIVE.md` (vor allem Packages 123 bis 142 und 172 bis 180).

## Überblick

Der Ton läuft auf zwei Ebenen:

- **`ovs_audio.dll`** (Rust, `native/ovs_audio`): Geräte (WASAPI), Opus (libopus 1.3.1), Echounterdrückung (WebRTC AEC3), Rauschunterdrückung (nnnoiseless, die Rust-Fassung von RNNoise), Jitter-Buffer, Mixer, Begrenzer und die Hinweistöne. `src/core` ist plattformneutral und mit `cargo test` ohne Gerät testbar, `src/device` enthält die Windows-Schicht.
- **C# im Client** (`src/OVS.Client/Audio`): Umrechnung und Zerlegung in Frames, Verstärkung, automatische Verstärkung, Sprachaktivierung, die Entscheidung, ob und wohin gesendet wird, die Anpassung an Paketverlust, Verschlüsselung und Versand. `Audio/Rust/` ist die Brücke zur DLL (C-ABI, jede Funktion `ovs_audio_*`, die ABI-Version wird beim Laden geprüft).

Die Rust-Seite ruft nie in .NET zurück. Sie füllt Ringpuffer und setzt Events, die C# abholt. Dadurch läuft der Ton weiter, auch wenn .NET kurz steht, etwa bei einer Garbage Collection.

Einheit überall: 48 kHz, Float-Samples, Frames von 20 ms (960 Samples pro Kanal).

Fehlt `ovs_audio.dll`, läuft der Client wie mit `--no-audio`: Verbinden und Chat gehen, Ton nicht, und der Client zeigt beim Start einen Hinweis.

## Senden: vom Mikrofon zum Server

1. **Aufnahme** (Rust, `device/wasapi.rs`, `microphone.rs`): Ein eigener Thread öffnet das Mikrofon über WASAPI im Shared Mode (Gerät per Windows-ID oder Standardgerät), mit 48 kHz Float in der Kanalzahl des Geräts; Windows rechnet um (`AUTOCONVERTPCM`). Der Thread ist ereignisgesteuert, bei MMCSS als "Pro Audio" angemeldet und hat einen Gerätepuffer von 20 ms. Er schreibt in einen Ring von einer Sekunde und setzt nach jeder Runde ein Event.
2. **Aufnahme-Thread in C#** (`Capture/CapturePipeline.cs`): wartet auf dieses Event und liest die Samples. Weil der Ring eine Sekunde hält, geht nichts verloren, wenn dieser Thread kurz steht; der Frame kommt dann nur später.
3. **Kanäle** (`Capture/SampleConversion.cs`, `StereoInput.cs`): Bei Sprache wird auf Mono gemischt. Ein Mikrofon, das sich als Stereo meldet, aber nur auf einer Seite Signal hat, wird mit vollem Pegel übernommen statt halbiert. Im Musik-Modus bleibt ein Stereo-Gerät Stereo.
4. **Resampling und Frames** (`SincResampler.cs`, `FrameChunker.cs`): bringt alles auf 48 kHz (bei 48 kHz reicht es durch) und schneidet 20-ms-Frames.
5. **Filter pro Frame**, in dieser Reihenfolge, nur bei Sprache (im Musik-Modus keiner):
   - **Echounterdrückung** (AEC3 in Rust, `Processing/EchoCanceller.cs`) auf dem rohen Signal, bevor die Verstärkung es nichtlinear macht. Ihr "fernes Ende" ist genau das, was der Lautsprecher gespielt hat (Stimmen nach dem Begrenzer plus Hinweistöne); der Render-Thread legt es ab, die Aufnahme holt es vor jedem Frame. Der Verzögerungshinweis setzt sich aus Wiedergabepuffer, Gerätepuffer des Lautsprechers und des Mikrofons zusammen (`AudioEngine.EchoDelayMs`).
   - **Eingangsverstärkung**: bis 100 % eine reine Multiplikation, darüber werden Spitzen über -3 dBFS weich gerundet (tanh), damit 200 % nicht knackt.
   - **Rauschunterdrückung** (nnnoiseless in Rust, `Processing/NoiseSuppressor.cs`): filtert Lüfter und Tippen und liefert eine Sprachwahrscheinlichkeit.
   - **Automatische Verstärkung** (`Processing/AutoGain.cs`): regelt nur, wo die Rauschunterdrückung Sprache erkennt.
6. **Sprachaktivierung** (`Transmit/VoiceActivityDetector.cs`): lernt ständig das Grundrauschen (10. Perzentil der Frame-Pegel der letzten 2 s, mal 1,5). Automatisch liegt die Schwelle beim Dreifachen davon (zwischen -60 und -28 dBFS, nach 0,3 s Lernzeit), sonst beim eingestellten Wert. Fällt der Pegel unter 0,7 mal die Schwelle, wird noch 180 ms weitergesendet. Bei Stereo zählt die lautere Seite. Der Pegel speist auch die Anzeige in den Einstellungen.
7. **Sendeentscheidung** (`Transmit/TransmitRule.cs`): Push-to-Talk sendet, solange die Taste gedrückt ist, Sprachaktivierung, solange sie Sprache hört; Link-PTT geht in beiden Modi und ist der einzige Weg in verlinkte Channels. Nichts geht raus bei stummem Mikrofon, in einem stummen Channel oder ohne Sprachverbindung. Beginnt eine Übertragung, geht der Frame davor mit, damit der Wortanfang nicht abgeschnitten wird.
8. **Opus** (`Codec/VoiceEncoder.cs` auf libopus in `core/encoder.rs`): Komplexität 5, kein DTX, FEC bei Sprache. Die Bitrate ist die des eigenen Channels. Musik nutzt Opus' Musik-Modus, Stereo ab 64 kbit/s.
9. **Anpassung an Verlust** (`Transmit/LossAdapter.cs`): Der Server meldet alle 2 s den Verlust auf dem Weg zu ihm. Opus erwartet das 1,5-Fache davon (5 bis 30 %) und legt entsprechend FEC bei. Liegt der Verlust 5 s über 10 %, halbiert sich die Bitrate (bis 16 kbit/s); nach 10 s unter 2 % verdoppelt sie sich wieder, höchstens bis zum Wert des Channels.
10. **Versand** (`OVS.Shared/Voice`): Jedes Paket ist `[Typ 1][Session-ID 4][Folgenummer 4][Ziel 1][verschlüsselter Opus-Frame][GCM-Tag 16]`, verschlüsselt mit AES-256-GCM und dem Sitzungsschlüssel, den der Server über TLS vergeben hat. Die 10 Kopfbytes sind mit authentifiziert, die Richtung steckt in der Nonce. Ziel 0 ist der eigene Channel, 1 der eigene plus die verlinkten. Klappt UDP nicht, geht die Sprache über die TLS-Verbindung (`VoiceUp`), bis UDP wieder antwortet.

Der Selbsttest nimmt denselben Weg bis einschliesslich Opus und spielt das Ergebnis lokal ab, statt es zu senden.

## Server: verteilen

`OVS.Server/Voice/UdpVoiceServer.cs` entschlüsselt jedes Paket, prüft Absenderadresse (nur die der eigenen Verbindung, bei IPv6 dasselbe /64), Grösse und Rate und verschlüsselt den Frame für jeden Empfänger neu mit dessen Schlüssel. Wer was hört, entscheidet `VoiceRouting.cs`: den eigenen Channel, mit dem Recht "Über Links sprechen" und Ziel 1 auch die direkt verlinkten. Aus einem stummen Channel und aus dem AFK-Channel (solange die AFK-Verschiebung an ist) wird nichts weitergeleitet; Link-Sprache in einen stummen Channel hinein schon.

Empfängern, die Verlust melden, hängt der Server den vorigen Frame als Kopie an (Package 136), ohne zusätzliche Verschlüsselung. Empfänger über TLS bekommen nie Kopien, der Weg verliert nichts. Der Server dekodiert kein Opus und mischt nichts.

## Empfangen: vom Server zum Lautsprecher

1. **Empfang** (C#): Der Client entschlüsselt das Paket und reicht Frame, Folgenummer und gegebenenfalls die Kopie über einen begrenzten Kanal an den Mixer in Rust (`Playback/VoiceMixer.cs`, `Rust/NativeSpeaker.cs`). Der Render-Thread wartet nie auf diesen Kanal.
2. **Jitter-Buffer pro Sprecher** (`core/jitter.rs`): sortiert die Frames nach Folgenummer und startet nach 2 Frames (40 ms) Vorlauf, höchstens 10 Frames (200 ms) Puffer. Verspätete Frames lassen den Vorlauf wachsen, in ruhigen Phasen klingt er wieder ab. Nach einer Sprechpause puffert er neu. Fehlt ein Frame, wird er in dieser Reihenfolge ersetzt: aus der FEC des nächsten Pakets, aus der Kopie des Servers, sonst durch Opus' Verschleierung (PLC). Die Zahlen dazu zeigt die Verbindungsansicht des Clients.
3. **Dekodieren** (`core/decoder.rs`): libopus, immer nach Stereo; ein Mono-Paket klingt auf beiden Seiten gleich.
4. **Mixer** (`core/mixer.rs`): summiert alle Sprecher zu einem 20-ms-Frame, jeden mit seiner Lautstärke. Die Lautstärke pro Person (0 bis 200 %), die Gesamtlautstärke und der Empfangs-Boost von +6 dB werden in C# (`Processing/VolumeCurve.cs`) zu einem Faktor pro Sprecher verrechnet und bei jeder Änderung übergeben. Bei Taubschaltung ist der Mix stumm, ausser der eigenen Stimme im Selbsttest. Bis zu 64 Sprecher haben vorab angelegte Puffer; wer 60 s still war, fällt heraus.
5. **Begrenzer** (`core/limiter.rs`): Liegt ein Frame über 0,95, wird er als Ganzes leiser gemacht; danach steigt die Verstärkung um 0,05 pro Frame wieder an, Sample für Sample, damit nichts klickt. Darunter bleibt das Signal unverändert.
6. **Wiedergabe** (`core/playout.rs`, `core/ring.rs`, `device/wasapi.rs`): Ein eigener Render-Thread spielt über WASAPI im Shared Mode, 48 kHz Float Stereo, ereignisgesteuert, MMCSS "Pro Audio", Gerätepuffer 30 ms. Er ruft den Mixer genau dann, wenn das Gerät Daten will, und mischt die Hinweistöne obendrauf. Was fehlt, wird als Stille gespielt und gezählt.

Wer gerade spricht, den Pegel und die Zähler legt der Render-Thread in einen Schnappschuss, den die Wiedergabeschleife in C# (`Playback/PlaybackLoop.cs`) alle 20 ms liest und an die Oberfläche meldet (grüner und violetter Ring).

## Hinweistöne

`Sounds/SoundPlayer.cs` und `SoundLibrary.cs` wählen den Ton zum Ereignis: eingebaut oder eigene WAV- oder MP3-Datei bis 5 Sekunden, mit Lautstärke pro Ton und "Alle Sounds aus". Eigene Dateien liest NAudio (nur dafür ist es noch dabei). Beim ersten Abspielen lädt der Client die Samples einmal in die DLL. Ab da spielt der Render-Thread einen begonnenen Ton bis zum Ende, egal was .NET macht; höchstens 16 gleichzeitig, ein weiterer ersetzt den ältesten. Ein Ton beginnt genau an der Stelle, an der der nächste Sprach-Frame gespielt wird, damit Ton, Stimmen und das ferne Ende der Echounterdrückung zusammenpassen. Beim Verbinden ist der Lautsprecher schon offen, damit "Verbunden" nicht verschluckt wird.

## Geräte

`Devices/DeviceLifecycle.cs` öffnet Mikrofon und Lautsprecher nur, solange sie gebraucht werden (verbunden oder im Selbsttest), und öffnet sie neu, wenn sich das gewählte Gerät ändert oder eines ausfällt. Die Liste der Geräte und das Beobachten von An- und Abstecken kommen aus NAudios `CoreAudioApi`. "Testen" in den Einstellungen spielt eine Sekunde einen Ton auf dem gewählten Lautsprecher, ohne Verbindung.

## Bauen und testen

`powershell -File native\ovs_audio\build.ps1` baut `ovs_audio.dll` (Voraussetzungen im Kopf des Skripts) und legt sie unter `src/OVS.Client/runtimes/win-x64/native/` ab, wo sie eingecheckt ist. `cargo test` testet den Kern ohne Gerät. Die C#-Tests (`dotnet test`) prüfen den Ton über dieselben Schnittstellen, die der Client nutzt, mit einem Fake-Lautsprecher und Fake-Mikrofon, ohne hörbaren Ton.
