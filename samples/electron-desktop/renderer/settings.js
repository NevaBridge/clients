"use strict";

const field = (id) => document.getElementById(id);

function showProblem(error) {
  field("problem").textContent = String(
    error && error.message ? error.message : error,
  ).replace(/^Error invoking remote method '[^']+': /, "");
  field("problem").hidden = false;
}

window.example.loadSettings().then((saved) => {
  if (!saved) {
    return;
  }
  field("product-id").value = saved.productId;
  field("environment").value = saved.environment;
  field("webchat-url").value = saved.webChatUrl;
  field("api-key").placeholder = "Saved (leave empty to keep it)";
}, showProblem);

field("settings").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    await window.example.saveSettings({
      apiKey: field("api-key").value,
      productId: field("product-id").value,
      environment: field("environment").value,
      webChatUrl: field("webchat-url").value,
    });
    window.close();
  } catch (error) {
    showProblem(error);
  }
});

field("remove").addEventListener("click", async () => {
  await window.example.removeSettings();
  window.close();
});

field("cancel").addEventListener("click", () => window.close());
