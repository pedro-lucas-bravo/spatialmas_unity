using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using SpatialMAS.Logging;
using UnityEngine.TestTools;

namespace SpatialMAS.Tests.PlayMode
{
    public class WifiLogExportServerTests
    {
        [UnityTest]
        public IEnumerator WifiLogServer_GroupsSessionsAndDownloadsOnlyTheSelectedCompletedSession()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_WifiTest_" + System.Guid.NewGuid().ToString("N"));
            using var recorder = new DataRecordingService(null, tempDir);
            using var server = new WifiLogExportServer(recorder);
            using var http = new HttpClient();
            try
            {
                recorder.StartRecording("first");
                string firstDirectory = Path.GetDirectoryName(recorder.ActiveMetadataPath);
                string timestamp = Path.GetFileName(firstDirectory);
                string first = timestamp + "_" + recorder.SessionId + "_first";
                recorder.LogOscMessage("IN", "/first", "1");
                recorder.StopRecording();
                // Recreate the original flat layout alongside a new folder-based session.
                foreach (string file in Directory.GetFiles(firstDirectory))
                    File.Move(file, Path.Combine(tempDir, Path.GetFileNameWithoutExtension(file) +
                        "_" + first + Path.GetExtension(file)));
                Directory.Delete(firstDirectory);
                recorder.StartRecording("second");
                string secondDirectory = Path.GetDirectoryName(recorder.ActiveMetadataPath);
                string secondFolder = Path.GetFileName(secondDirectory);
                string second = "folder-" + secondFolder;
                File.WriteAllText(Path.Combine(tempDir, "older.csv"), "standalone log");

                Assert.IsTrue(server.StartServer(0));
                string url = $"http://127.0.0.1:{server.Port}";
                var dashboard = http.GetStringAsync(url);
                while (!dashboard.IsCompleted) yield return null;
                StringAssert.Contains("<details>", dashboard.Result);
                StringAssert.DoesNotContain("<strong>" + first + "</strong>", dashboard.Result);
                StringAssert.Contains(" UTC</strong>", dashboard.Result);
                StringAssert.Contains("/sessions/" + first + ".zip", dashboard.Result);
                StringAssert.DoesNotContain("/sessions/" + second + ".zip", dashboard.Result);
                StringAssert.Contains("/logs/older.csv", dashboard.Result);
                StringAssert.Contains("/logs/osc_traffic_" + first + ".csv", dashboard.Result);
                StringAssert.Contains("/logs/" + secondFolder + "/osc_traffic.csv", dashboard.Result);

                var active = http.GetAsync(url + "/sessions/" + second + ".zip");
                while (!active.IsCompleted) yield return null;
                using (var response = active.Result)
                    Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);

                var download = http.GetAsync(url + "/sessions/" + first + ".zip");
                while (!download.IsCompleted) yield return null;
                using (var response = download.Result)
                {
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                    Assert.AreEqual("application/zip", response.Content.Headers.ContentType.MediaType);
                    Assert.AreEqual(timestamp + ".zip", response.Content.Headers.ContentDisposition.FileNameStar);
                    var body = response.Content.ReadAsByteArrayAsync();
                    while (!body.IsCompleted) yield return null;
                    using var zip = new ZipArchive(new MemoryStream(body.Result), ZipArchiveMode.Read);
                    var expected = Directory.GetFiles(tempDir, "*" + first + ".*")
                        .Select(path => timestamp + "/" + Path.GetFileName(path)).ToArray();
                    Assert.AreEqual(6, expected.Length);
                    CollectionAssert.AreEquivalent(expected, zip.Entries.Select(entry => entry.FullName).ToArray());
                    foreach (var entry in zip.Entries)
                    {
                        using var reader = new StreamReader(entry.Open());
                        Assert.AreEqual(File.ReadAllText(Path.Combine(tempDir, entry.Name)), reader.ReadToEnd());
                    }
                }

                recorder.StopRecording();
                var completed = http.GetAsync(url + "/sessions/" + second + ".zip");
                while (!completed.IsCompleted) yield return null;
                using (var response = completed.Result)
                {
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                    Assert.AreEqual(secondFolder + ".zip", response.Content.Headers.ContentDisposition.FileNameStar);
                    var body = response.Content.ReadAsByteArrayAsync();
                    while (!body.IsCompleted) yield return null;
                    using var zip = new ZipArchive(new MemoryStream(body.Result), ZipArchiveMode.Read);
                    var expected = Directory.GetFiles(secondDirectory)
                        .Select(path => secondFolder + "/" + Path.GetFileName(path)).ToArray();
                    Assert.AreEqual(6, expected.Length);
                    CollectionAssert.AreEquivalent(expected, zip.Entries.Select(entry => entry.FullName).ToArray());
                }
            }
            finally
            {
                server.StopServer();
                recorder.StopRecording();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [UnityTest]
        public IEnumerator WifiLogServer_ServesDashboardAndCsvDownloadOverHttp()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SpatialMAS_WifiTest_" + System.Guid.NewGuid().ToString("N"));
            var recorder = new DataRecordingService(null, tempDir);
            recorder.StartRecording("wifi_test");
            recorder.LogOscMessage("IN", "/test/alive", "127.0.0.1,6012,1.0");
            recorder.Flush();

            var server = new WifiLogExportServer(recorder);
            // Bind to ephemeral port 0 so test never conflicts with other local processes
            bool started = server.StartServer(0);
            Assert.IsTrue(started);

            int boundPort = server.Port;
            Assert.Greater(boundPort, 0);

            string fileName = Path.GetFileName(recorder.ActiveOscCsvPath);
            string folder = Path.GetFileName(Path.GetDirectoryName(recorder.ActiveOscCsvPath));
            Task<string> jsonTask = null;
            Task<string> csvTask = null;

            using (var httpClient = new HttpClient())
            {
                jsonTask = httpClient.GetStringAsync($"http://127.0.0.1:{boundPort}/api/logs");
                while (!jsonTask.IsCompleted) yield return null;

                Assert.IsFalse(jsonTask.IsFaulted);
                StringAssert.Contains(fileName, jsonTask.Result);
                string metadataName = Path.GetFileName(recorder.ActiveMetadataPath);
                StringAssert.Contains(metadataName, jsonTask.Result);
                var metadataTask = httpClient.GetAsync($"http://127.0.0.1:{boundPort}/logs/{folder}/{metadataName}");
                while (!metadataTask.IsCompleted) yield return null;
                Assert.IsFalse(metadataTask.IsFaulted);
                using (var response = metadataTask.Result)
                {
                    Assert.IsTrue(response.IsSuccessStatusCode);
                    Assert.AreEqual("application/json", response.Content.Headers.ContentType.MediaType);
                    var bodyTask = response.Content.ReadAsStringAsync();
                    while (!bodyTask.IsCompleted) yield return null;
                    StringAssert.Contains("\"schemaVersion\":2", bodyTask.Result);
                }

                csvTask = httpClient.GetStringAsync($"http://127.0.0.1:{boundPort}/logs/{folder}/{fileName}");
                while (!csvTask.IsCompleted) yield return null;

                Assert.IsFalse(csvTask.IsFaulted);
                StringAssert.Contains("/test/alive", csvTask.Result);
            }

            server.Dispose();
            recorder.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
