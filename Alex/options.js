let page = document.getElementById('buttonDiv');
const kButtonColors = ['#3aa757', '#e8453c', '#f9bb2d', '#4688f1'];
function constructOptions(kButtonColors) {
  for (let item of kButtonColors) {
    let button = document.createElement('button');
    button.style.backgroundColor = item;
    button.addEventListener('click', function() {
      chrome.storage.sync.set({color: item}, function() {
        console.log('color is ' + item);
      })
    });
    page.appendChild(button);
  }
}
constructOptions(kButtonColors);

let submit = document.getElementById('submit');
let result = document.getElementById('result');
let input = document.getElementById('input');

submit.addEventListener('click', function() {
    var xmlHttp = new XMLHttpRequest();
    xmlHttp.open("GET", "https://mixer.com/api/v1/channels/" + input.value, false); // true for asynchronous 
    xmlHttp.send(null);
    console.log(xmlHttp.status)
    if (xmlHttp.status == 200) 
    {
        chrome.storage.sync.get('username', function(data) {
            if (!(data.username === ""))
            {
                console.log('username is' + data.username)
                var request = new XMLHttpRequest();
                request.open("GET", "http://relay.quinndamerell.com/Blob.php?key=mixer-carl-" + data.username + "-active&data=", false); // true for asynchronous 
                request.send(null);
            }
            chrome.storage.sync.set({username: input.value}, function() {
                console.log('username is ' + input.value);
            })
            var request = new XMLHttpRequest();
            var date = new Date();
            var timestamp = date.toISOString();
            request.open("GET", "http://relay.quinndamerell.com/Blob.php?key=mixer-carl-" + input.value + "-active&data=" + timestamp, false); // true for asynchronous 
            request.send(null);
            console.log('posted data');
            console.log(request.status);
        });

        result.innerText = "Validated and a real user"
    }
    else 
    {
        result.innerText = "Non valid user name try again."
    }
});