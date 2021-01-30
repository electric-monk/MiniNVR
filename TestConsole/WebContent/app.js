var settingsWindow = null;
var activeCameras = {};

function generateGenericEntry(owner) {
  var cameraDiv = document.createElement("div");
  cameraDiv.classList.add("cameraUnselected");
  cameraDiv.onmousedown = function(e) {
    for (let entry of owner.childNodes) {
      entry.classList.remove("cameraSelected");
      entry.classList.add("cameraUnselected");
    }
    cameraDiv.classList.remove("cameraUnselected");
    cameraDiv.classList.add("cameraSelected");
    if (cameraDiv._selected)
      cameraDiv._selected();
  };
  return cameraDiv;
}

function generateCameraEntry(owner, identifier, entry) {
  var cameraDiv = generateGenericEntry(owner);
  cameraDiv.classList.add("cameraKnown");
  cameraDiv.innerHTML = '<img style="float: left;" src="/camera_2-0.png">' + entry['title'] + '<br>' + identifier;
  owner.insertBefore(cameraDiv, owner.firstChild);
  cameraDiv._identifier = identifier;
  cameraDiv._known = true;
  return cameraDiv;
}

function generateUnknownEntry(owner, name, address) {
  var cameraDiv = generateGenericEntry(owner);
  cameraDiv.classList.add("cameraUnknown");
  cameraDiv.innerHTML = '<img style="float: left;" src="/camera_2-0.png">' + name + '<br>' + address;
  owner.appendChild(cameraDiv);
  cameraDiv._identifier = address;
  cameraDiv._known = false;
  return cameraDiv;
}

function generateCameraSetup(content, buttonText) {
  const formdata = [
    "General",
        ["Friendly name", "text", "title"],
    "Credentials",
        ["Username", "text", "username"],
        ["Password", "password", "password"],
    "Settings",
        ["Record this source", "checkbox", "record"],
    "&nbsp;",
        [buttonText, "button", "save"]
  ];
  if (content == null)
    content = {};
  let form = document.createElement("form");
  let fieldset = document.createElement("fieldset");
  form.appendChild(fieldset);
  fieldset.style.border = 0;
  let values = {};
  form._fields = values;
  form._fieldset = fieldset;
  {
    let table = document.createElement("table");
    fieldset.appendChild(table);
    form.container = table;
    for (let item of formdata) {
      let tr = document.createElement("tr");
      table.appendChild(tr);
      if (!form.topentry)
        form.topentry = tr;
      let td = document.createElement("td");
      tr.appendChild(td);
      if (typeof(item) == "string") {
        td.setAttribute("colspan", 2);
        td.innerHTML = "<b>" + item + "</b>";
      } else {
        let input = document.createElement("input");
        input.setAttribute("type", item[1]);
        if (content[item[2]])
          input.setAttribute("value", content[item[2]]);
        input.setAttribute("id", item[2]);
        values[item[2]] = input;
        let label = document.createElement("label");
        label.setAttribute("for", item[2]);
        label.innerHTML = item[0];
        if (item[1] == "checkbox") {
          td.setAttribute("colspan", 2);
          td.appendChild(input);
          td.appendChild(label);
          input._readValue = () => input.checked;
        } else if (item[1] == "button") {
          td.setAttribute("colspan", 2);
          td.setAttribute("align", "right");
          input.setAttribute("value", item[0]);
          td.appendChild(input);
        } else {
          td.setAttribute("align", "right");
          td.appendChild(label);
          let td2 = document.createElement("td");
          tr.appendChild(td2);
          td2.appendChild(input);
          input._readValue = () => input.value;
        }
      }
    }
  }
  return form;
}

function generateKnownCameraSetup(identifier, values) {
  let form = generateCameraSetup(values, "Update");
  return form;
}

function generateUnknownCameraSetup(ip, newdata) {
  const fields = [["Manufacturer", newdata["Manufacturer"]], ["Model", newdata["Model"]], ["IP Address", ip], ["", ""]];
  let form = generateCameraSetup(null, "Add");
  for (var x of fields) {
    let tr = document.createElement("tr");
    form.container.insertBefore(tr, form.topentry);
    let td = document.createElement("td");
    tr.appendChild(td);
    td.setAttribute("align", "right");
    td.innerHTML = x[0];
    let td2 = document.createElement("td");
    tr.appendChild(td2);
    td2.innerHTML = "<b>" + x[1] + "</b>";
  }
  return form;
}

function scrubChildren(el) {
  while (el.firstChild)
    el.removeChild(el.firstChild);
}

function generateSettingsCamera(dialog) {
  var cameraList = document.createElement("div");
  cameraList.classList.add("cameraList");
  cameraList._update = function(cameras){
    let previous = [];
    for (let existing of cameraList.childNodes) {
      if (!existing._known)
        continue;
      previous.push(existing._identifier);
    }
    console.log("Known:");
    console.log(previous);
    for (let camera in cameras) {
      if (previous.includes(camera))
        previous = previous.filter((e) => e != camera);
      let values = cameras[camera];
      let found = null;
      for (let existing of cameraList.childNodes) {
        if (existing._identifier == camera) {
          found = existing;
          break;
        }
      }
      if (found != null) {
        if (found._known)
          continue;
        found.parentElement.removeChild(found);
      }
      let entry = generateCameraEntry(cameraList, camera, values);
      entry._selected = function() {
        scrubChildren(cameraSettings);
        let setup = generateKnownCameraSetup(camera, values);
        cameraSettings.appendChild(setup);
        setup._fields.save.onclick = function(e) {
        };
      };
    }
    for (let item of cameraList.childNodes)
      if (previous.includes(item._identifier))
        item.parentElement.removeChild(item);
  };
  cameraList._update(activeCameras)
  dialog.appendChild(cameraList);
  let cameraSettings = document.createElement("div");
  cameraSettings.classList.add("cameraSettings");
  cameraSettings.innerHTML = "Form";
  dialog.appendChild(cameraSettings);
  let seen = [];
  cameraList._updateSearch = function(data){
    for (let cameraIp in data) {
      if (seen.includes(cameraIp))
        continue;
      seen.push(cameraIp);
      let camera = data[cameraIp];
      let cameraMfr = data[cameraIp]["Manufacturer"];
      let cameraMdl = data[cameraIp]["Model"];
      let cameraEntry = generateUnknownEntry(cameraList, cameraMfr + " " + cameraMdl, cameraIp);
      cameraEntry._selected = function() {
        scrubChildren(cameraSettings);
        let setup = generateUnknownCameraSetup(cameraIp, camera);
        cameraSettings.appendChild(setup);
        setup._fields.save.onclick = function(e) {
          setup._fieldset.setAttribute("disabled", "disabled");
          let jdata = {
            'identifier': cameraIp,
            'endpoint': data[cameraIp]["Endpoints"][0]
          };
          for (let item in setup._fields)
            if (setup._fields[item]._readValue)
              jdata[item] = setup._fields[item]._readValue();
          exchangeJSON("/updateCamera", jdata, function(status, data) {
            if (status == 200) {
              scrubChildren(cameraSettings);
              cameraSettings.innerHTML = "<center>Camera added!</center>";
            } else {
              setup._fieldset.removeAttribute("disabled");
              alert("Failed to add camera");
            }
          });
        };
      }
    }
  };
  return cameraList;
}

function showSettings(e) {
  if (settingsWindow == null) {
    settingsWindow = createWindow(400, 300, WIN_TITLE | WIN_MOVABLE | WIN_CLOSABLE | WIN_ACTIVATE | WIN_SIZABLE);
    getWindowTitle(settingsWindow).innerHTML = '<img src="gears-1.png">&nbsp;Settings';
    var tabs = createTabs();
    getWindowContent(settingsWindow).appendChild(tabs);
    var cameras = generateSettingsCamera(tabs._addTab("Cameras"));
    settingsWindow._reloadCameras = (d) => cameras._update(d);
    var users = tabs._addTab("Users");
    users.innerHTML = "Under construction";
    settingsWindow._discoverer = new PeriodicLoader("/discovery", 1000);
    settingsWindow._discoverer.callback = data => cameras._updateSearch(data);
    settingsWindow._discoverer.start();
    settingsWindow._onclosed = function() {
      settingsWindow._discoverer.stop();
      settingsWindow = null;
    }
    document.body.appendChild(settingsWindow);
  } else {
    settingsWindow._activate();
  }
}

function createCameraMenu() {
  let cameras = createMenu();
  cameras._known = [];
  cameras._button.innerHTML = '<img src="camera_2-0.png">';
  cameras._reload = function(data) {
    if (data.length == 0) {
      let item = menuAddItem(cameras, "No cameras available");
      item._selected = () => showSettings(null);
    } else {
      let leftover = cameras._known;
      let newknown = [];
      for (let item in data) {
        newknown.push(item);
        let index = leftover.indexOf(item);
        if (index != -1) {
          leftover.splice(index, 1);
          continue;
        }
        let values = data[item];
        let menuItem = menuAddItem(cameras, values.title);
        menuItem._identifier = item;
        menuItem._selected = function() {
          let cameraWindow = createWindow(400, 300);
          getWindowTitle(cameraWindow).innerHTML = '<img src="camera_2-1.png">&nbsp;' + values.title;
          getWindowContent(cameraWindow).innerHTML = '<video class="cameraView" autoplay src="/stream-' + item + '">Video missing</video>';
          document.body.appendChild(cameraWindow);
        };
      }
      cameras._known = newknown;
      for (let item in leftover)
        for (let existing of cameras.childNodes)
          if (existing._identifier == item)
            cameras.removeChild(existing);
    }
  };
  return cameras;
}

toolbar = createToolbar();
let cameras = createCameraMenu();
toolbar.appendChild(cameras._container);

var recordings = createButton(true);
recordings.innerHTML = '<img src="backup_devices_2-0.png">';
toolbar.appendChild(recordings);

var settings = createButton(false);
settings.innerHTML = '<img src="gears-0.png">';
toolbar.appendChild(settings);
settings._triggered = showSettings

document.body.appendChild(toolbar);

cameraMonitor = new PeriodicLoader("/allCameras", 1000);
cameraMonitor.callback = function(data) {
  activeCameras = data;
  cameras._reload(data);
  if (settingsWindow != null)
    settingsWindow._reloadCameras(data);
};
cameraMonitor.start();
