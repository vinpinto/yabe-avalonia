Name:           yabe-avalonia
Version:        1.0.0
Release:        1%{?dist}
Summary:        BACnet device browser and diagnostic tool
License:        MIT
URL:            https://github.com/vinpinto/yabe-avalonia
Source0:        %{name}-%{version}.tar.gz
AutoReqProv:    no

%global debug_package %{nil}
%define __strip /bin/true
%define __brp_strip /bin/true
%define __brp_strip_static_archive /bin/true
%define __brp_strip_comment_note /bin/true

%description
Cross-platform port of YABE (Yet Another BACnet Explorer) using
Avalonia UI. Supports BACnet/IP, IPv6, MSTP, PTP and Ethernet
discovery, COV subscriptions, trend logs, alarms, schedules and more.

%prep
%setup -q

%install
cp -a . %{buildroot}

%files
/opt/%{name}/
%attr(755,-,-) /usr/bin/%{name}
/usr/share/applications/%{name}.desktop
/usr/share/icons/hicolor/256x256/apps/%{name}.png
