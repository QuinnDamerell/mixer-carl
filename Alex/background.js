chrome.runtime.onInstalled.addListener(function() {
    fetch("https://mixer.com/api/v1/users/current").then(function(response) {
        return response.json();
     }).then(function(data) {
       console.log(data);
     }).catch(function() {
       console.log("Booo");
     });

    chrome.storage.sync.set({})
    chrome.storage.sync.set({color: '#3aa757'}, function() {
      console.log("The color is green.");
    });
    
chrome.declarativeContent.onPageChanged.removeRules(undefined, function() {
    chrome.declarativeContent.onPageChanged.addRules([{
        conditions: [new chrome.declarativeContent.PageStateMatcher({
        pageUrl: {hostEquals: 'mixer.com'},
        })
        ],
            actions: [new chrome.declarativeContent.ShowPageAction()]
    }]);
    });
});