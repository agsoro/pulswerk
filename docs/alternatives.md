# Alternatives & Comparison

When building an industrial automation and energy management monitoring system, there are several platforms available. Pulswerk was intentionally designed to address the common pain points found in industry-standard solutions.

Here is how **Pulswerk** compares to the alternatives.

## ThingsBoard

[ThingsBoard](https://thingsboard.io/) is a massive, widely-used open-source IoT platform for data collection, processing, and device management. 

**Where ThingsBoard Excels:**
- Enormous ecosystem and integrations.
- Complex rule engines and data processing pipelines.
- Multi-tenant enterprise architectures.

**Why Pulswerk Wins:**
- **Zero Configuration Overhead**: ThingsBoard requires setting up device profiles, assets, complex rule chains, and dashboard aliases just to see a single data point. Pulswerk auto-discovers devices and builds the hierarchy for you instantly. 
- **Simplicity**: ThingsBoard's UI can be overwhelmingly complex for standard building automation operators. Pulswerk provides a modern, fast, and highly intuitive UI tailored specifically for SCADA and building metrics.
- **Resource Footprint**: ThingsBoard is a heavy Java application that demands significant RAM and CPU. Pulswerk is written in highly optimized .NET 8, resulting in a microscopic memory footprint, fast startup times, and easy edge-deployment.
- **Protocol Focus**: Pulswerk has deep, native, out-of-the-box drivers specifically engineered for BACnet/IP and Modbus TCP, parsing complex property states without custom payload decoders.

## Home Assistant

[Home Assistant](https://www.home-assistant.io/) is an incredibly popular open-source home automation platform.

**Where Home Assistant Excels:**
- Thousands of consumer-grade device integrations (Zigbee, WiFi plugs, consumer HVAC).
- End-user mobile app experience.
- Easy to use visual automations.

**Why Pulswerk Wins:**
- **Industrial Focus**: Home Assistant is built for the smart home. Pulswerk is built for industrial and commercial environments. 
- **Data Retention**: Home Assistant relies heavily on a relational database (SQLite/MariaDB) which struggles with the massive throughput of industrial high-frequency polling. Pulswerk natively pipes telemetry into **InfluxDB**, effortlessly handling millions of data points and natively supporting time-series aggregation.
- **Professional Protocols**: Pulswerk supports advanced Modbus and BACnet architectures (COV, structured views) which are severely limited or non-existent in consumer-oriented software.

## Ignition (Inductive Automation)

[Ignition](https://inductiveautomation.com/) is a powerful, industry-standard SCADA platform.

**Where Ignition Excels:**
- Massive industrial deployments.
- Extremely powerful Python scripting engine.
- Deep integration with Allen-Bradley, Siemens S7, and OPC-UA.

**Why Pulswerk Wins:**
- **Open Source & Free**: Ignition is highly expensive commercial software requiring restrictive licensing modules. Pulswerk is 100% free and open-source under the GPLv3.
- **Modern Tech Stack**: Pulswerk’s web dashboard uses modern, responsive web technologies out-of-the-box without requiring heavy designer clients or proprietary perspective builders.
- **Simplicity**: Setting up tags and historians in Ignition takes time and specialized training. Pulswerk gives you immediate historical trending automatically.

## Siemens Desigo CC

[Desigo CC](https://www.siemens.com/global/en/products/buildings/automation/desigo/building-management/desigo-cc.html) is the proprietary building management platform developed by Siemens, widely deployed in large-scale commercial and industrial buildings.

**Where Desigo CC Excels:**
- Native integration with proprietary Siemens hardware ecosystems.
- Extremely extensive enterprise-grade HVAC logic and fire safety handling.
- Large-scale facility management spanning massive portfolios.

**Why Pulswerk Wins:**
- **Open Source & Hardware Agnostic**: Desigo CC heavily drives vendor lock-in towards Siemens equipment and incurs extremely high licensing costs. Pulswerk is an independent, 100% open-source tool that treats BACnet and Modbus hardware from any vendor as equal first-class citizens.
- **Modern User Experience**: Desigo CC relies on a monolithic, legacy-feeling interface that can be slow and clunky. Pulswerk provides a blazing fast, web-native UI that works flawlessly on mobile devices out-of-the-box.
- **Data Liberation**: Extracting raw telemetry out of Desigo CC for modern external analytics often requires expensive API licenses. Pulswerk uses InfluxDB natively, giving you absolute ownership and zero-barrier access to all your historical data.

## Summary

If you need a massive multi-tenant platform with infinite rule chains, use **ThingsBoard**.
If you want to turn on your living room lights, use **Home Assistant**.
If you have a massive budget and need OPC-UA integration, use **Ignition**.
If you are strictly locked into the Siemens enterprise ecosystem, use **Desigo CC**.

**If you want a fast, simple, open-source, and modern gateway to visualize your Modbus and BACnet devices instantly—use Pulswerk.**
