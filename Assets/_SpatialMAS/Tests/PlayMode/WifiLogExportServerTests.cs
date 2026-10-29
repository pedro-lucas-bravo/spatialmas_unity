using System.Collections;
using System.IO;
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
                var metadataTask = httpClient.GetAsync($"http://127.0.0.1:{boundPort}/logs/{metadataName}");
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

                csvTask = httpClient.GetStringAsync($"http://127.0.0.1:{boundPort}/logs/{fileName}");
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
