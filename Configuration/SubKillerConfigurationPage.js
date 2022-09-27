define(["loading", "dialogHelper", "mainTabsManager", "formDialogStyle", "emby-checkbox", "emby-select", "emby-toggle", "emby-collapse"],
    function(loading, dialogHelper, mainTabsManager) {

        const pluginId = "700B24C7-A6ED-4164-905D-C39622D08868";

        function getTabs() {
            return [{
                    href: Dashboard.getConfigurationPageUrl('SubKillerPluginConfigurationPage'),
                    name: 'Sub-Killer'
                }
            ];
        }

        var currentUserId;

        async function getUser() {
            currentUserId = await ApiClient.getCurrentUserId();
            console.log(currentUserId);
            /*return new Promise((resolve, reject) => {
                (ApiClient.getCurrentUserId()).then(result => {
                    resolve(result);
                    console.log(result);
                    currentUserId = result;
                });
            });*/
        }

        function getSeries(currentUserId) {
            return new Promise((resolve, reject) => {
                ApiClient.getJSON(ApiClient.getUrl('Users/' + currentUserId + '/Views?IncludeExternalContent=false')).then(result => {
                    resolve(result);
                });
            });
        }

        function getLibraries(currentUserId) {
            return new Promise((resolve, reject) => {
                //Users/0b5c75352b49432fb380fd239f7a7984/Items?Recursive=false&ExcludeItemTypes=Channel%2C&IncludeItemTypes=Movie%2C%20Series&api_key=3a5f67e9e11a46cfa0694f7eaa5ff03c
                ApiClient.getJSON(ApiClient.getUrl('Users/' + currentUserId + '/Items?Recursive=false&ExcludeItemTypes=Channel%2C&IncludeItemTypes=Movie%2C%20Series')).then(result => {
                    resolve(result);
                });
            });
        }

        async function getBaseItem(id) {
            return await ApiClient.getJSON(ApiClient.getUrl('Items?Ids=' + id));
        }

        function getListItemHtml(library, padding) {

            var html = '';

            if (library) {
                html += '<div class="virtualScrollItem listItem listItem-border focusable listItemCursor listItem-hoverable listItem-withContentWrapper" tabindex="0" draggable="false" style="transform: translate(0px, ' + padding + 'px);">';
                html += '<div class="listItem-content listItemContent-touchzoom">';
                html += '<div class="listItemBody itemAction listItemBody-noleftpadding">';
                html += '<div class="listItemBodyText listItemBodyText-nowrap">' + library.Name + '</div>';
                html += '<div class="listItemBodyText listItemBodyText-secondary listItemBodyText-nowrap">Will be included for Subtitle Removal Task.</div>';
                html += '</div>';
                html += '<button title="Remove" aria-label="Remove" type="button" is="paper-icon-button-light" class="listItemButton itemAction paper-icon-button-light icon-button-conditionalfocuscolor removeItemBtn" id="' + library.Id + '">';
                html += '<i class="md-icon removeItemBtn" style="pointer-events: none;">delete</i>';
                html += '</button> ';
                html += '</div>';
                html += '</div>';
            }
            return html;
        }

        function handleRemoveItemClick(e, element, view) {
            var id = e.target.closest('button').id;
            ApiClient.getPluginConfiguration(pluginId).then((config) => {
                var filteredList = config.LibrariesToConvert.filter(item => item !== id);
                config.LibrariesToConvert = filteredList;
                ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                    reloadList(filteredList, element, view);
                    loadSeriesSelect(config, view);
                    Dashboard.processPluginConfigurationUpdateResult(r);
                });

            });

        }

        function reloadList(list, element, view) {
            if (element != null) {
                element.innerHTML = '';
            }
            if (list && list.length) {
                var padding = 0;
                list.forEach(async id => {
                    var result = await getBaseItem(id);
                    var baseItem = result.Items[0];
                    element.innerHTML += getListItemHtml(baseItem, padding);
                    padding += 77; // Necessary for spacing the items added to the list
                    var removeButtons = view.querySelectorAll('.removeItemBtn');
                    removeButtons.forEach(btn => {
                        btn.addEventListener('click',
                            el => {
                                el.preventDefault();
                                handleRemoveItemClick(el, element, view);
                            });
                    });

                });
            }
        }

        function loadSeriesSelect(config, view) {
            var librarySelect = view.querySelector('#selectEmbySeries');
            librarySelect.innerHTML = '';
            getSeries(currentUserId).then(series => {
                var seriesItems = series.Items;
                console.log("Library Items: ", seriesItems);
                for (let i = 0; i <= seriesItems.length - 1; i++) {
                    if (config.LibrariesToConvert.includes(parseInt(seriesItems[i].Id)) || config.LibrariesToConvert.includes(seriesItems[i].Id)) {
                        continue;
                    }
                    librarySelect.innerHTML += `<option value="${seriesItems[i].Id}">${seriesItems[i].Name}</option>`;
                }
            });
        }


        return function(view) {
            view.addEventListener('viewshow', async() => {

                loading.show();
                getUser();
                mainTabsManager.setTabs(this, 0, getTabs);

                var config = await ApiClient.getPluginConfiguration(pluginId);
                var selectedSubs = view.querySelectorAll(".chkLanguage");

                ApiClient.getPluginConfiguration(pluginId).then(function(config) {

                    view.querySelector(".chkEnableSubKiller").checked = config.EnableSubKiller;
                    view.querySelector(".chkRunSubKillerOnItemAdded").checked = config.RunSubKillerOnItemAdded;
                    view.querySelector(".chkEnableSubtitleExtract").checked = config.EnableSubTitleExtract;
                    view.querySelector(".chkEnableExtractForced").checked = config.EnableExtractForced;
                    view.querySelector(".chkEnableSubKillerRefresh").checked = config.EnableSubKillerRefresh;
                    view.querySelector("#txtselectedLangs").textContent = config.SelectedLanguages.replace(/[\[\]']+/g, '') || "";

                    if (config.LibrariesToConvert) {
                        reloadList(config.LibrariesToConvert, includeLibrary, view);
                    }

                    loadSeriesSelect(config, view);
                    var storedLang = config.SelectedLanguages.replace(/[\[\]']+/g, '');
                    let storedLanguage = storedLang.split(",");
                    console.log("Stored languages", storedLanguage);
                    for (var i = 0; i < selectedSubs.length; i++) {
                        var selectedSubElement = selectedSubs[i];
                        var selectedAltSub = selectedSubElement.getAttribute("data-altlang");
                        console.log("selected Lang: ", selectedAltSub);
                        if (storedLanguage.includes(selectedAltSub)) {
                            selectedSubElement.checked = true;
                        }
                    }
                });

                loading.hide();

                var includeLibrary = view.querySelector('.ignore-list');
                var librarySelect = view.querySelector('#selectEmbySeries');
                var addToLibraryListBtn = view.querySelector('#btnAddSeriesToIgnoreList');

                var enableSubKiller = view.querySelector(".chkEnableSubKiller");
                enableSubKiller.addEventListener('change',
                    (e) => {
                        e.preventDefault();
                        ApiClient.getPluginConfiguration(pluginId).then((config) => {
                            config.EnableSubKiller = enableSubKiller.checked;
                            ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                                Dashboard.processPluginConfigurationUpdateResult(r);
                            });
                        });
                    });

                var autoRunSubKillerOnItemAdded = view.querySelector(".chkRunSubKillerOnItemAdded");
                autoRunSubKillerOnItemAdded.addEventListener('change',
                    (e) => {
                        e.preventDefault();
                        ApiClient.getPluginConfiguration(pluginId).then((config) => {
                            config.RunSubKillerOnItemAdded = autoRunSubKillerOnItemAdded.checked;
                            ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                                Dashboard.processPluginConfigurationUpdateResult(r);
                            });
                        });
                    });

                var enableSubtitleExtract = view.querySelector(".chkEnableSubtitleExtract");
                enableSubtitleExtract.addEventListener('change',
                    (e) => {
                        e.preventDefault();
                        ApiClient.getPluginConfiguration(pluginId).then((config) => {
                            config.EnableSubTitleExtract = enableSubtitleExtract.checked;
                            ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                                Dashboard.processPluginConfigurationUpdateResult(r);
                            });
                        });
                    });

                var enableExtractForced = view.querySelector(".chkEnableExtractForced");
                enableExtractForced.addEventListener('change',
                    (e) => {
                        e.preventDefault();
                        ApiClient.getPluginConfiguration(pluginId).then((config) => {
                            config.EnableExtractForced = enableExtractForced.checked;
                            ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                                Dashboard.processPluginConfigurationUpdateResult(r);
                            });
                        });
                    });

                var enableSubKillerRefresh = view.querySelector(".chkEnableSubKillerRefresh");
                enableSubKillerRefresh.addEventListener('change',
                    (e) => {
                        e.preventDefault();
                        ApiClient.getPluginConfiguration(pluginId).then((config) => {
                            config.EnableSubKillerRefresh = enableSubKillerRefresh.checked;
                            ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                                Dashboard.processPluginConfigurationUpdateResult(r);
                            });
                        });
                    });


                selectedSubs.forEach(function(chk) {
                    chk.addEventListener('change',
                        function(e) {
                            e.preventDefault();
                            ApiClient.getPluginConfiguration(pluginId).then((config) => {
                                config.SelectedLanguages = [];
                                selectedSubs.forEach(function(chk) {
                                    if (chk.checked) {
                                        config.SelectedLanguages.push(chk.getAttribute("data-altlang"));
                                    }
                                });
                                ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                                    Dashboard.processPluginConfigurationUpdateResult(r);
                                });
                                view.querySelector("#txtselectedLangs").textContent = config.SelectedLanguages || "";
                            });
                        });
                });

                addToLibraryListBtn.addEventListener('click', (el) => {
                    el.preventDefault();

                    loading.show();

                    var libraryId = librarySelect[librarySelect.selectedIndex].value;
                    console.log("LibraryId: ", libraryId);

                    ApiClient.getPluginConfiguration(pluginId).then((config) => {

                        if (config.LibrariesToConvert) {

                            config.LibrariesToConvert.push(libraryId);
                            console.log(libraryId.toString());

                        } else {

                            config.LibrariesToConvert = [libraryId];

                        }
                        ApiClient.updatePluginConfiguration(pluginId, config).then((r) => {
                            reloadList(config.LibrariesToConvert, includeLibrary, view);
                            loadSeriesSelect(config, view);
                            Dashboard.processPluginConfigurationUpdateResult(r);
                        });

                    });

                    loading.hide();
                });


            });

        }
    });