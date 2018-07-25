let changeColor = document.getElementById('changeColor');
let userName = document.getElementById('username');

console.log("About to run hello");

chrome.storage.sync.get('username', function(data) {
    console.log("Hello world");
    userName.innerText = "Current summons for:" + data.username;
    //changeColor.setAttribute('value', data.color);
});

chrome.storage.sync.get('color', function(data) {
    changeColor.style.backgroundColor = data.color;
    changeColor.setAttribute('value', data.color);
});

changeColor.onclick = function(element) {
    var newURL = "https://mixer.com/kabby";
    chrome.tabs.create({ url: newURL });
};