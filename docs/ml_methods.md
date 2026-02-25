# Machine Learning Methods of Market Analysis

Core Idea:
Enable an agent to create and run experiments building and training ML models to analyze market conditions. 

As we stream market data, we need to be logging it to an archival database. 
There should be an agent that has read access to this database and can devise and run experiments looking for indicators. 

This would look something like: 
- There is a ML Research+Quantitative Analysis agent (RQ)
- RQ agent day dreams about possible ways of building predictive models to predict market direction on both the macro and micro scale
- Implement a model in pytorch
- Train and validate the model on the ever growing historical market dataset
- When winning models are found, promote the model as an indicator signal that is consumed and by the research agent. 

Other thoughts:
- Its possible that the the research signals will create noise, shouting out the model signal
- RQ Agent 