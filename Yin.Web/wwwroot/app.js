const form = document.getElementById("renderForm");
const modeSelect = document.getElementById("mode");
const templateSelect = document.getElementById("templateName");
const imageInput = document.getElementById("imageInput");
const fileName = document.getElementById("fileName");
const statusText = document.getElementById("status");
const submitButton = document.getElementById("submitButton");
const preview = document.querySelector(".preview");
const previewImage = document.getElementById("previewImage");
const downloadLink = document.getElementById("downloadLink");
const password = document.getElementById("password");

let templateData = null;
let currentObjectUrl = null;

imageInput.addEventListener("change", () => {
  fileName.textContent = imageInput.files[0]?.name || "选择图片";
});

modeSelect.addEventListener("change", populateTemplates);

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!imageInput.files.length) return;

  submitButton.disabled = true;
  statusText.textContent = "正在生成...";

  const formData = new FormData(form);
  const headers = {};
  if (password.value) {
    headers["X-Yin-Password"] = password.value;
  }

  try {
    const response = await fetch("/api/render", {
      method: "POST",
      headers,
      body: formData
    });

    if (!response.ok) {
      const message = await response.text();
      throw new Error(message || `生成失败：${response.status}`);
    }

    const blob = await response.blob();
    if (currentObjectUrl) URL.revokeObjectURL(currentObjectUrl);
    currentObjectUrl = URL.createObjectURL(blob);

    const disposition = response.headers.get("content-disposition") || "";
    const match = disposition.match(/filename\*=UTF-8''([^;]+)|filename="?([^"]+)"?/i);
    const outputName = match ? decodeURIComponent(match[1] || match[2]) : "Yin_Output.jpg";

    previewImage.src = currentObjectUrl;
    downloadLink.href = currentObjectUrl;
    downloadLink.download = outputName;
    preview.classList.add("has-image");
    statusText.textContent = "生成完成";
  } catch (error) {
    statusText.textContent = error.message;
  } finally {
    submitButton.disabled = false;
  }
});

loadTemplates();

async function loadTemplates() {
  const response = await fetch("/api/templates");
  templateData = await response.json();
  populateTemplates();
}

function populateTemplates() {
  if (!templateData) return;
  const mode = modeSelect.value.toLowerCase();
  const templates = templateData.templates[mode] || [];
  const defaultName = templateData.defaults[mode] || templates[0]?.name || "";

  templateSelect.innerHTML = "";
  for (const template of templates) {
    const option = document.createElement("option");
    option.value = template.name;
    option.textContent = template.name;
    option.selected = template.name === defaultName;
    templateSelect.append(option);
  }
}
