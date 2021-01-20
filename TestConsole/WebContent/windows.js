/* Windows.js/.css
 * Copyright (C) 2021 Colin Munro
 */

const WIN_SIZABLE = 1;
const WIN_MOVABLE = 2;
const WIN_TITLE = 4;
const WIN_CLOSABLE = 8;
const WIN_ACTIVATE = 16;

function createWindow(width, height, flags = WIN_SIZABLE | WIN_MOVABLE | WIN_TITLE | WIN_CLOSABLE | WIN_ACTIVATE) {
  // Construct window frame
  var win = document.createElement("div");
  win.classList.add("window");
  win.style.width = width + "px";
  win.style.height = height + "px";
  // Find window Z level, and deactivate all windows if necessary
  var allWindows = document.getElementsByClassName("window");
  var zLevel = allWindows.length;
  if (zLevel == 0)
    flags |= WIN_ACTIVATE;
  if (flags & WIN_ACTIVATE) {
    for (let other of allWindows)
      if ((other != win) && other._title)
        other._title.classList.add("windowTitleInactive");
  } else {
    zLevel = 0;
    for (let other of allWindows)
        other.style.zIndex = other.style.zIndex + 1;
  }
  win.style.zIndex = zLevel;
  // Focusishness
  function activateWindow(e) {
    var allWindows = document.getElementsByClassName("window");
    var currentZLevel = win.style.zIndex;
    for (let other of allWindows) {
      if (other._title)
        other._title.classList.add("windowTitleInactive");
      if (other.style.zIndex >= currentZLevel)
        other.style.zIndex = other.style.zIndex - 1;
    }
    win.style.zIndex = allWindows.length - 1;
    win._title.classList.remove("windowTitleInactive");
  }
  // Window position support
  var lastX = 0, lastY = 0;
  function dragMouseUp(e) {
    document.onmouseup = null;
    document.onmousemove = null;
    dragMouseMove(e);
  }
  function dragMouseMove(e) {
    e.preventDefault();
    var xOffset = lastX - e.clientX;
    var yOffset = lastY - e.clientY;
    lastX = e.clientX;
    lastY = e.clientY;
    win.style.top = (win.offsetTop - yOffset) + "px";
    win.style.left = (win.offsetLeft - xOffset) + "px";
  }
  function dragMouseDown(e) {
    e.preventDefault();
    document.onmouseup = dragMouseUp;
    document.onmousemove = dragMouseMove;
    lastX = e.clientX;
    lastY = e.clientY;
    activateWindow(e);
  }
  // Window size support
  var sizeX = 0, sizeY = 0;
  function sizeMouseUp(e) {
    document.onmouseup = null;
    document.onmousemove = null;
    sizeMouseMove(e);
  }
  function sizeMouseMove(e) {
    e.preventDefault();
    var xOffset = sizeX - e.clientX;
    var yOffset = sizeY - e.clientY;
    sizeX = e.clientX;
    sizeY = e.clientY;
    let newX = (win.offsetWidth - xOffset);
    if (newX < 250)
      newX = 250;
    let newY = (win.offsetHeight - yOffset);
    if (newY < 250)
      newY = 250;
    win.style.height = newY + "px";
    win.style.width = newX + "px";
  }
  function sizeMouseDown(e) {
    e.preventDefault();
    document.onmouseup = sizeMouseUp;
    document.onmousemove = sizeMouseMove;
    sizeX = e.clientX;
    sizeY = e.clientY;
  }
  // Construct window doodahs
  if (flags & WIN_TITLE) {
    var title = document.createElement("div");
    title.classList.add("windowTitle");
    win.appendChild(title);
    // Title area
    var text = document.createElement("div");
    text.style.float = "left";
    title.appendChild(text);
    // Close button
    if (flags & WIN_CLOSABLE) {
      var closeButton = document.createElement("div");
      closeButton.style.float = "right";
      closeButton.innerHTML = "<b>&#x2715;</b>";
      closeButton.onclick = function() {
        win.parentElement.removeChild(win);
      }
      title.appendChild(closeButton);
    }
    // Spacer
    var spacer = document.createElement("div");
    spacer.style = "clear: both;";
    title.appendChild(spacer);
    win._title = title;
    // Drag title
    if (flags & WIN_MOVABLE) {
      title.onmousedown = dragMouseDown;
    } else {
      title.onmousedown = activateWindow;
    }
    if (!(flags & WIN_ACTIVATE)) {
      title.classList.add("windowTitleInactive");
    }
  }
  var content = document.createElement("div");
  content.classList.add("windowContent");
  win.appendChild(content);
  win._content = content;
  // Resize corner
  if (flags & WIN_SIZABLE) {
    var sizer = document.createElement("div");
    sizer.innerHTML = "&#x21d8;";
    sizer.style.fontSize = "200%";
    sizer.style.position = "absolute";
    sizer.style.right = 0;
    sizer.style.bottom = 0;
    sizer.style.border = "1px solid green;";
    win.appendChild(sizer);
    sizer.onmousedown = sizeMouseDown;
  }
  // Done
  return win;
}

function getWindowTitle(win) {
  if (win._title)
    return win._title.childNodes[0];
  return null;
}

function getWindowContent(win) {
  return win._content;
}
